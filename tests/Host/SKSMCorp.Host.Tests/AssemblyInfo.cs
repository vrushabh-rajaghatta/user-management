// Every test in this assembly runs against one shared PostgreSQL database and
// counts or mutates rows in it. xUnit parallelises across collections by
// default, which previously let one class observe another's fixture rows and
// fail intermittently. The suite is fast; determinism is worth more than the
// wall-clock saving.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
