import { z } from "zod";

/**
 * AUT-Q6's response: the permission catalogue, which the release owns (PE2).
 * AUT-C7's picker is its first consumer — the read has existed since the role
 * administration slice with no screen behind it.
 */
export const permissionCatalogueSchema = z.object({
  permissions: z.array(
    z.object({
      permissionId: z.string().min(1),
      code: z.string().min(1),
      name: z.string().min(1),
      resource: z.string().min(1),
      action: z.string().min(1),
      requiresHumanActor: z.boolean(),
      isActive: z.boolean(),
    }),
  ),
});

export type PermissionCatalogue = z.infer<typeof permissionCatalogueSchema>;

export type CatalogueEntry = PermissionCatalogue["permissions"][number];

/** AUT-C7's answer: the new grant's id, and nothing else. */
export const grantedPermissionSchema = z.object({
  rolePermissionId: z.string().min(1),
});
