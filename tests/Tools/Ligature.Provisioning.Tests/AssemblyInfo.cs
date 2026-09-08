// These tests create and drop real databases and mutate the LIGATURE_CONNECTION
// environment variable, which is process-wide. Running them in parallel would
// have one test's connection string decide another test's target database.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
