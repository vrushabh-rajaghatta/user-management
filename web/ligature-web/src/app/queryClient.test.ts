import { describe, expect, it } from "vitest";
import { ApiContractError, ApiError } from "@/shared/api/errors";
import { createQueryClient, shouldRetryQuery } from "./queryClient";

describe("the query client", () => {
  it.each([400, 401, 403, 404])("never retries a query that failed with %i", (status) => {
    expect(shouldRetryQuery(0, new ApiError("http", status, "refused"))).toBe(false);
  });

  it("never retries a contract violation", () => {
    expect(shouldRetryQuery(0, new ApiContractError("GET", "/api/users", ["userId: required"]))).toBe(false);
  });

  it.each([
    ["a server error", new ApiError("http", 500, "failed")],
    ["a network failure", new ApiError("network", null, "unreachable")],
  ])("retries %s at most twice", (_, error) => {
    expect(shouldRetryQuery(0, error)).toBe(true);
    expect(shouldRetryQuery(1, error)).toBe(true);
    expect(shouldRetryQuery(2, error)).toBe(false);
  });

  it("applies that policy to queries and never retries mutations", () => {
    const options = createQueryClient().getDefaultOptions();

    expect(options.queries?.retry).toBe(shouldRetryQuery);
    expect(options.mutations?.retry).toBe(false);
  });
});
