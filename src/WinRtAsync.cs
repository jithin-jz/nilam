using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace Nilam
{
    /// <summary>
    /// Bridges WinRT IAsyncOperation to Task.
    ///
    /// The usual route is the AsTask/GetAwaiter extensions in
    /// System.Runtime.WindowsRuntime, but those are compiled against the unified
    /// Windows.WinMD identity, and the union facade shipped on many machines is
    /// too old to contain Windows.Media.Control. Referencing the individual winmd
    /// files in System32 gets us the media session types, and this helper
    /// replaces the extensions that then no longer bind.
    /// </summary>
    internal static class WinRtAsync
    {
        internal static Task<T> ToTask<T>(IAsyncOperation<T> operation)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            if (operation == null)
            {
                tcs.SetResult(default(T));
                return tcs.Task;
            }

            // Assigning Completed after the operation has already finished still
            // invokes the handler, so there is no race to guard against here.
            operation.Completed = delegate(IAsyncOperation<T> op, AsyncStatus status)
            {
                try
                {
                    if (status == AsyncStatus.Completed) tcs.TrySetResult(op.GetResults());
                    else if (status == AsyncStatus.Canceled) tcs.TrySetCanceled();
                    else
                    {
                        // Pass the operation's own error on: it carries the
                        // HRESULT, which is the only clue to what a player did.
                        Exception error = op.ErrorCode;
                        tcs.TrySetException(error != null
                            ? error
                            : new InvalidOperationException("WinRT operation failed."));
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };
            return tcs.Task;
        }
    }
}
