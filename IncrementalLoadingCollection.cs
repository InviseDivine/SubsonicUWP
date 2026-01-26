using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;

namespace SubsonicUWP
{
    public class IncrementalLoadingCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
    {
        private Func<uint, Task<IEnumerable<T>>> _loadFunction;
        private bool _hasMoreItems = true;

        public IncrementalLoadingCollection(Func<uint, Task<IEnumerable<T>>> loadFunction)
        {
            _loadFunction = loadFunction;
        }

        public bool HasMoreItems => _hasMoreItems;

        public void Reset()
        {
            _hasMoreItems = true;
        }

        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return AsyncInfo.Run(async (c) =>
            {
                // If cancellation requested, stop
                if (c.IsCancellationRequested) return new LoadMoreItemsResult { Count = 0 };

                try 
                {
                    var result = await _loadFunction(count);
                    uint actualCount = 0;

                    if (result != null)
                    {
                        foreach (var item in result)
                        {
                             Add(item);
                             actualCount++;
                        }
                        
                        // Only mark as finished if we got 0 items successfully (meaning success but no data)
                        if (actualCount == 0)
                        {
                            _hasMoreItems = false;
                        }
                    }
                    else
                    {
                        // Result was null -> Error occurred.
                        // Do NOT set _hasMoreItems = false. Keep it true so we can retry.
                        // We return 0 count, which stops the View from asking immediately.
                        // The UI should trigger a retry (e.g. via timer calling simple LoadMoreItemsAsync or scrolling).
                    }

                    return new LoadMoreItemsResult { Count = actualCount };
                }
                catch
                {
                    // Exception -> Error. Keep HasMoreItems = true.
                    return new LoadMoreItemsResult { Count = 0 };
                }
            });
        }
    }
}
