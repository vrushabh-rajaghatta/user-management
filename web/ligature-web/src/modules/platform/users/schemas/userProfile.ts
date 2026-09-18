import { z } from "zod";

/**
 * USR-Q1 GetUser v1's response (docs/requirements.md, "USR-C2 — Update User
 * Profile, and USR-Q1 GetUser (narrow v1)"): exactly these four fields.
 */
export const userProfileSchema = z.object({
  userId: z.string().min(1),
  firstName: z.string(),
  lastName: z.string(),
  displayName: z.string(),
});

export type UserProfile = z.infer<typeof userProfileSchema>;

/**
 * The Edit profile form: PRESENCE ONLY (USR-C2 UI, U3). A field must be a
 * non-empty string, and that is all. No trim, no control-character check, no
 * length limit: JavaScript and .NET trim different characters, and a length
 * the browser counts in UTF-16 units is not the server's code points — so any
 * of those would reject input the server accepts (§12). The server owns the
 * rules; its refusal is shown word for word.
 */
export const profileFormSchema = z.object({
  firstName: z.string().min(1, "A first name is required."),
  lastName: z.string().min(1, "A last name is required."),
  displayName: z.string().min(1, "A display name is required."),
});

export type ProfileForm = z.infer<typeof profileFormSchema>;
