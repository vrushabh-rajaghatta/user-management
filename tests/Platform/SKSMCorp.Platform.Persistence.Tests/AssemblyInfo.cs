using Xunit;

// These are integration tests against ONE shared PostgreSQL database, which is
// global mutable state. xUnit runs test classes in parallel by default, so a
// class that counts rows — CatalogueDriftTests compares the seeded catalogue
// against the whole table — intermittently observed another class's fixture
// rows and failed. The suite was measurably flaky: 2 failures, then two clean
// runs, from identical code.
//
// Parallelism is therefore disabled for this assembly. The unit-test
// assemblies, which touch nothing shared, still run in parallel.
//
// The alternative — teaching every counting assertion to exclude other tests'
// fixtures — would put the burden on each new test and fail silently when
// someone forgot.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
