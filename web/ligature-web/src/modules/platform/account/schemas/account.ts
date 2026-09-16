import { z } from "zod";

/**
 * What each endpoint returns, checked at the boundary
 * (docs/frontend-architecture.md §16). The identifier is not shown to anyone;
 * it is declared because the response carries it, and a response that does not
 * match its declaration is a contract violation rather than an undefined that
 * surfaces later.
 */
export const accountChangedSchema = z.object({ userIdentityId: z.string().min(1) });

/**
 * CRD-C2 answers 200 with one fixed sentence whatever was typed. The sentence
 * is shown to the visitor exactly as it arrives.
 */
export const resetRequestedSchema = z.object({ message: z.string().min(1) });
