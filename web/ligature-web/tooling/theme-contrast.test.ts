import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

/**
 * Theme tokens against WCAG 2.2 AA (docs/frontend-architecture.md §15).
 *
 * A deterministic token invariant — token pair, contrast ratio, threshold —
 * run against the real src/index.css. It is a separate contract from axe, which
 * checks rendered semantics and cannot compute contrast in jsdom. This proves
 * the tokens; it does not prove every rendered combination.
 *
 * Every token a pair names must exist: a missing token fails, rather than a
 * pair silently dropping out of the check.
 */

const STYLESHEET = path.resolve(import.meta.dirname, "..", "src", "index.css");

const TEXT = 4.5;
const NON_TEXT = 3;

/** [foreground token, surface token]: text drawn on that surface. */
const TEXT_PAIRS: [string, string][] = [
  ["foreground", "background"],
  ["card-foreground", "card"],
  ["popover-foreground", "popover"],
  ["primary-foreground", "primary"],
  ["secondary-foreground", "secondary"],
  ["accent-foreground", "accent"],
  ["muted-foreground", "background"],
  ["muted-foreground", "card"],
  ["muted-foreground", "muted"],
  ["destructive", "background"],
];

/**
 * [token, surface]: non-text UI that must be perceivable (WCAG 1.4.11). The
 * focus indicator (ring) and the only visible boundary of a control (input), on
 * every surface they are used on.
 */
const NON_TEXT_PAIRS: [string, string][] = [
  ["ring", "background"],
  ["ring", "card"],
  ["input", "background"],
  ["input", "card"],
  ["input", "muted"],
];

/**
 * Exempt, and named so the exemption is a decision rather than an omission.
 * --border draws dividers and the outlines of containers that are identified by
 * other means; it is decorative, not a required non-text boundary. Anything that
 * is the only visible boundary of a control uses --input, which is checked above.
 */
const DECORATIVE = ["border"];

type Rgb = [number, number, number];

describe("the theme tokens", () => {
  const stylesheet = readFileSync(STYLESHEET, "utf8");

  for (const [theme, selector] of [
    ["light", ":root"],
    ["dark", ".dark"],
  ] as const) {
    const tokens = readTokens(stylesheet, selector);

    describe(`in the ${theme} theme`, () => {
      it.each(TEXT_PAIRS)(`give --%s text on --%s at least ${String(TEXT)}:1`, (foreground, surface) => {
        expect(contrast(tokens, foreground, surface)).toBeGreaterThanOrEqual(TEXT);
      });

      it.each(NON_TEXT_PAIRS)(`give --%s on --%s at least ${String(NON_TEXT)}:1`, (token, surface) => {
        expect(contrast(tokens, token, surface)).toBeGreaterThanOrEqual(NON_TEXT);
      });
    });
  }

  it("treat --border as decorative, and never as a required boundary", () => {
    const checked = [...TEXT_PAIRS, ...NON_TEXT_PAIRS].flat();

    expect(DECORATIVE).toEqual(["border"]);
    expect(checked.filter((token) => DECORATIVE.includes(token))).toEqual([]);
  });

  it("fail when a token a pair names is missing", () => {
    expect(() => contrast(new Map(), "foreground", "background")).toThrow(/--foreground/);
  });

  /**
   * The contract is only as good as the arithmetic behind it, so the arithmetic
   * is checked against known WCAG answers with synthetic tokens — independent of
   * whatever the theme's values happen to be. The translucent case matters: no
   * theme token in the matrix is translucent today, so without it the
   * compositing path would go unexercised.
   */
  it("compute WCAG ratios correctly, including a translucent token over its surface", () => {
    const synthetic = new Map<string, Oklch>([
      ["black", { l: 0, c: 0, h: 0, alpha: 1 }],
      ["white", { l: 1, c: 0, h: 0, alpha: 1 }],
      ["half-black", { l: 0, c: 0, h: 0, alpha: 0.5 }],
    ]);

    expect(contrast(synthetic, "black", "white")).toBeCloseTo(21, 2);
    expect(contrast(synthetic, "white", "white")).toBeCloseTo(1, 5);

    // sRGB 0.5 over white: linear 0.21404, so (1 + 0.05) / (0.21404 + 0.05).
    expect(contrast(synthetic, "half-black", "white")).toBeCloseTo(3.977, 2);
  });

  /**
   * A stylesheet check, not a rendered one. The vendored primitives draw their
   * focus ring at 50% opacity, which cannot reach 3:1, so the visible focus
   * indicator is an application-level solid outline in the ring token.
   */
  it("draw keyboard focus as a solid outline in the ring token", () => {
    expect(stylesheet).toMatch(/:focus-visible\s*\{[^}]*outline:\s*2px\s+solid\s+var\(--ring\)/);
    expect(stylesheet).not.toMatch(/outline-ring\/50/);
  });

  it("respect a reduced-motion preference", () => {
    expect(stylesheet).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)/);
  });
});

// ------------------------------------------------------------------ colour

type Oklch = { l: number; c: number; h: number; alpha: number };

function readTokens(stylesheet: string, selector: string): Map<string, Oklch> {
  const escaped = selector.replace(".", "\\.");
  const block = new RegExp(`(^|\\n)${escaped}\\s*\\{([^}]*)\\}`).exec(stylesheet);

  if (block?.[2] === undefined) {
    throw new Error(`src/index.css has no ${selector} block.`);
  }

  const tokens = new Map<string, Oklch>();
  const declaration = /--([a-z0-9-]+):\s*oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*(?:\/\s*([\d.]+)%)?\s*\)/g;

  for (const match of block[2].matchAll(declaration)) {
    const [, name, l, c, h, alpha] = match;

    if (name !== undefined && l !== undefined && c !== undefined && h !== undefined) {
      tokens.set(name, { l: Number(l), c: Number(c), h: Number(h), alpha: alpha === undefined ? 1 : Number(alpha) / 100 });
    }
  }

  return tokens;
}

function contrast(tokens: Map<string, Oklch>, token: string, surface: string): number {
  const colour = find(tokens, token);
  const background = toSrgb(find(tokens, surface));
  const drawn = colour.alpha < 1 ? composite(toSrgb(colour), colour.alpha, background) : toSrgb(colour);

  const [lighter, darker] = [luminance(drawn), luminance(background)].sort((a, b) => b - a) as [number, number];

  return (lighter + 0.05) / (darker + 0.05);
}

function find(tokens: Map<string, Oklch>, name: string): Oklch {
  const token = tokens.get(name);

  if (token === undefined) {
    throw new Error(`The theme does not define --${name}, which a contrast pair requires.`);
  }

  return token;
}

/** OKLCH to gamma-encoded sRGB (Björn Ottosson's reference transform). */
function toSrgb({ l, c, h }: Oklch): Rgb {
  const a = c * Math.cos((h * Math.PI) / 180);
  const b = c * Math.sin((h * Math.PI) / 180);

  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;

  const linear: Rgb = [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ];

  return linear.map((v) => {
    const clamped = Math.min(1, Math.max(0, v));
    return clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * clamped ** (1 / 2.4) - 0.055;
  }) as Rgb;
}

function composite([r, g, b]: Rgb, alpha: number, [br, bg, bb]: Rgb): Rgb {
  return [r * alpha + br * (1 - alpha), g * alpha + bg * (1 - alpha), b * alpha + bb * (1 - alpha)];
}

/** WCAG relative luminance of a gamma-encoded sRGB colour. */
function luminance(colour: Rgb): number {
  const [r, g, b] = colour.map((channel) =>
    channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4,
  ) as Rgb;

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
