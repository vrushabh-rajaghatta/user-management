import { api } from "@/shared/api/client";
import { userProfileSchema, type ProfileForm } from "../schemas/userProfile";

/** USR-Q1 GetUser v1: GET /api/users/{userId}, user.read. */
export const getUserProfile = (userId: string, signal?: AbortSignal) =>
  api.get(`/api/users/${encodeURIComponent(userId)}`, { response: userProfileSchema, signal });

/**
 * USR-C2: POST /api/users/{userId}/profile, user.update. Sends exactly what
 * was typed; the server trims, validates and stores. 204 with no body, change
 * or not.
 */
export const updateUserProfile = ({ userId, ...names }: { userId: string } & ProfileForm) =>
  api.post(`/api/users/${encodeURIComponent(userId)}/profile`, { body: names });
