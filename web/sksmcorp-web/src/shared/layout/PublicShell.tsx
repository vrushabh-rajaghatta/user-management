import { Link, Outlet } from "react-router";

/** The shell for pages anyone may open: the home page today, account flows later. */
export function PublicShell() {
  return (
    <div className="flex min-h-svh flex-col">
      <header className="border-b">
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center px-4 sm:px-6">
          <Link to="/" className="font-semibold">
            SKSMCorp
          </Link>
        </div>
      </header>
      <main id="main" tabIndex={-1} className="flex-1">
        <Outlet />
      </main>
    </div>
  );
}
