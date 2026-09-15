/**
 * The composition root (docs/frontend-architecture.md §2).
 *
 * A placeholder while the foundation is scaffolded: no router, no providers,
 * no authentication state and no API calls yet. Those arrive with the
 * application behaviour they serve, not ahead of it.
 */
export function App() {
  return (
    <main className="mx-auto flex min-h-svh max-w-2xl flex-col justify-center gap-3 px-6">
      <h1 className="text-2xl font-semibold tracking-tight">Ligature</h1>
      <p className="text-muted-foreground">
        The web client foundation is being built. Nothing can be done here yet.
      </p>
    </main>
  );
}
