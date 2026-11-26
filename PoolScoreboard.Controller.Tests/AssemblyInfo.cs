using Xunit;

// The Controller tests share process-wide state: Trace.Listeners is a single global collection that several
// classes attach to and detach from while another enumerates it (which throws "Collection was modified"), and
// the overlay tests bind real loopback sockets. Running classes in parallel made the suite fail intermittently
// for reasons unrelated to what a test asserts, and the whole assembly takes a few seconds anyway.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
