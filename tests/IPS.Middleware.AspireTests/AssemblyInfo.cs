using Xunit;

// Each scenario starts containers; running them one after another keeps the machine from being overloaded, which would
// otherwise make the tests, not the service, the thing under test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
