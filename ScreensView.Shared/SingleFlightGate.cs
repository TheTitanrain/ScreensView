using System.Threading;

namespace ScreensView.Shared;

public sealed class SingleFlightGate
{
    private int _busy;

    public Lease? TryEnter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return null;

        return new Lease(this);
    }

    public sealed class Lease : IDisposable
    {
        private readonly SingleFlightGate _owner;
        private int _disposed;

        internal Lease(SingleFlightGate owner) => _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Volatile.Write(ref _owner._busy, 0);
        }
    }
}
