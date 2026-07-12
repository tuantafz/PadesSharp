// Original implementation based on public standards, no code copied from iText 5/7.

using System;
using System.Collections.Concurrent;
using System.Threading;
using ModernPdf.Abstractions.Pkcs11;

namespace ModernPdf.Pkcs11
{
    /// <summary>
    /// Thread-safe pool of <see cref="IPkcs11Session"/> objects.
    /// Sessions are created lazily on demand and returned to the pool after use.
    /// An unhealthy session (one that threw during a sign operation) is discarded
    /// rather than returned, and a fresh session is opened on the next rent.
    /// </summary>
    public sealed class Pkcs11SessionPool : IDisposable
    {
        private readonly IPkcs11SessionFactory _factory;
        private readonly Pkcs11SessionRequest _request;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentQueue<IPkcs11Session> _available;
        private bool _disposed;

        /// <summary>
        /// Initialises the pool.
        /// </summary>
        /// <param name="factory">Factory used to open new sessions.</param>
        /// <param name="request">Session parameters passed to the factory.</param>
        /// <param name="maxSessions">Maximum concurrent sessions. Default uses <c>request.MaxSessions</c>.</param>
        public Pkcs11SessionPool(
            IPkcs11SessionFactory factory,
            Pkcs11SessionRequest request,
            int? maxSessions = null)
        {
            _factory  = factory  ?? throw new ArgumentNullException(nameof(factory));
            _request  = request  ?? throw new ArgumentNullException(nameof(request));
            int cap   = maxSessions ?? request.MaxSessions;
            if (cap < 1) throw new ArgumentOutOfRangeException(nameof(maxSessions), "Must be at least 1.");
            _semaphore  = new SemaphoreSlim(cap, cap);
            _available  = new ConcurrentQueue<IPkcs11Session>();
        }

        /// <summary>
        /// Rents a session from the pool, blocking until one becomes available or the token is cancelled.
        /// Dispose the returned <see cref="Pkcs11SessionLease"/> to return the session.
        /// </summary>
        public Pkcs11SessionLease Rent(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Pkcs11SessionPool));

            _semaphore.Wait(cancellationToken);

            IPkcs11Session session;
            if (!_available.TryDequeue(out session!))
                session = _factory.OpenSession(_request);

            return new Pkcs11SessionLease(this, session);
        }

        /// <summary>
        /// Called by <see cref="Pkcs11SessionLease.Dispose"/> to return a session.
        /// Healthy sessions go back into the available queue; unhealthy ones are disposed.
        /// </summary>
        internal void Return(IPkcs11Session session, bool healthy)
        {
            if (healthy && !_disposed)
            {
                _available.Enqueue(session);
            }
            else
            {
                try { session.Dispose(); } catch { /* swallow — pool must not throw on return */ }
            }

            // Release the slot so the next waiter can proceed.
            if (!_disposed)
            {
                try { _semaphore.Release(); }
                catch (ObjectDisposedException) { /* pool disposed between check and release */ }
            }
        }

        /// <summary>Disposes all pooled sessions and the internal semaphore.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            while (_available.TryDequeue(out var s))
            {
                try { s.Dispose(); } catch { }
            }

            _semaphore.Dispose();
        }
    }

    /// <summary>
    /// A rented <see cref="IPkcs11Session"/> that is automatically returned to the
    /// <see cref="Pkcs11SessionPool"/> when disposed.
    /// Mark <see cref="IsHealthy"/> = <c>false</c> before disposing to discard the session.
    /// </summary>
    public sealed class Pkcs11SessionLease : IDisposable
    {
        private readonly Pkcs11SessionPool _pool;
        private bool _disposed;

        /// <summary>The rented session.</summary>
        public IPkcs11Session Session { get; }

        /// <summary>
        /// Set to <c>false</c> before disposing to signal the pool that the session
        /// is no longer usable (e.g. after a failed signing attempt).
        /// Defaults to <c>true</c>.
        /// </summary>
        public bool IsHealthy { get; set; } = true;

        internal Pkcs11SessionLease(Pkcs11SessionPool pool, IPkcs11Session session)
        {
            _pool   = pool;
            Session = session;
        }

        /// <summary>Returns the session to the pool (or disposes it if unhealthy).</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pool.Return(Session, IsHealthy);
        }
    }
}
