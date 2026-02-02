using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Data.Json;
using System.Linq;

namespace SubsonicUWP
{
    public class SessionManager
    {
        private const string FILE_NAME = "sessions.json";
        private const string STATE_FILE = "current_state.json";
        private static System.Threading.SemaphoreSlim _stateSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private static System.Threading.SemaphoreSlim _sessionSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        public static async Task SaveSession(string name, ObservableCollection<SubsonicItem> queue, bool isShuffle, int repeatMode)
        {
            await _sessionSemaphore.WaitAsync();
            try
            {
                var sessions = await LoadSessionsInternal();
                var existing = sessions.FirstOrDefault(s => s.Name == name);
                // Capture ID if overwriting
                Guid idToUse = existing?.Id ?? Guid.NewGuid();
                if (existing != null) sessions.Remove(existing);

                var newSession = new SavedSession 
                { 
                    Id = idToUse, // Reuse ID
                    Name = name, 
                    Created = DateTime.Now, 
                    Tracks = new List<SubsonicItem>(queue),
                    IsShuffle = isShuffle,
                    RepeatMode = repeatMode
                };
                sessions.Insert(0, newSession);

                await WriteSessionsInternal(sessions);
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        public static async Task UpdateSession(SavedSession session)
        {
            await _sessionSemaphore.WaitAsync();
            try
            {
                var sessions = await LoadSessionsInternal();
                var existing = sessions.FirstOrDefault(s => s.Id == session.Id);
                
                if (existing != null) sessions.Remove(existing);
                sessions.Insert(0, session); // Move to top or keep? Moved to top usually implies "Recent"
                
                await WriteSessionsInternal(sessions);
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        private static System.Threading.CancellationTokenSource _saveDebounceToken;

        public static void SaveCurrentState(ObservableCollection<SubsonicItem> queue, string name, int index, Guid sessionId, TimeSpan position, bool isShuffle, int repeatMode)
        {
            // Cancel previous pending save
            _saveDebounceToken?.Cancel();
            _saveDebounceToken = new System.Threading.CancellationTokenSource();
            var token = _saveDebounceToken.Token;

            // Clone collections to avoid thread conflict during delay
            // Note: Shallow copy of list is okay as items aren't modified, but collection might change
            var queueCopy = new List<SubsonicItem>(queue ?? new ObservableCollection<SubsonicItem>());

            // Fire and Forget Background Task with Debounce
            Task.Run(async () =>
            {
                try
                {
                    // Debounce: Wait 2 seconds to see if another change comes in
                    await Task.Delay(2000, token);
                    if (token.IsCancellationRequested) return;

                    await _stateSemaphore.WaitAsync(token);
                    try
                    {
                        if (queueCopy == null || queueCopy.Count == 0)
                        {
                            // If queue is empty, remove the state file
                            try
                            {
                                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(STATE_FILE);
                                await file.DeleteAsync();
                            }
                            catch (System.IO.FileNotFoundException) { }
                            return;
                        }

                        var session = new SavedSession
                        {
                            Id = sessionId,
                            Name = name,
                            Created = DateTime.Now,
                            Tracks = queueCopy,
                            CurrentIndex = index,
                            Position = position,
                            IsShuffle = isShuffle,
                            RepeatMode = repeatMode
                        };

                        using (var stream = new System.IO.MemoryStream())
                        {
                            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SavedSession));
                            serializer.WriteObject(stream, session);
                            stream.Position = 0;
                            using (var reader = new System.IO.StreamReader(stream))
                            {
                                var text = reader.ReadToEnd();
                                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(STATE_FILE, CreationCollisionOption.ReplaceExisting);
                                await FileIO.WriteTextAsync(file, text);
                            }
                        }
                    }
                    finally
                    {
                        _stateSemaphore.Release();
                    }
                }
                catch (OperationCanceledException) { } // Ignore cancel
                catch (Exception) { } // Silent fail for background save
            });
        }

        public static async Task<SavedSession> LoadCurrentState()
        {
            await _stateSemaphore.WaitAsync();
            try
            {
                try
                {
                    var file = await ApplicationData.Current.LocalFolder.GetFileAsync(STATE_FILE);
                    var text = await FileIO.ReadTextAsync(file);
                    using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
                    {
                        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SavedSession));
                        return (SavedSession)serializer.ReadObject(stream);
                    }
                }
                catch { return null; }
            }
            finally
            {
                 _stateSemaphore.Release();
            }
        }

        public static async Task<ObservableCollection<SavedSession>> LoadSessions()
        {
            await _sessionSemaphore.WaitAsync();
            try
            {
                var list = await LoadSessionsInternal();
                
                // Include Auto-Saved State
                var current = await LoadCurrentState();
                if (current != null)
                {
                    // Create a copy or modify display name for UI
                    current.Name = string.IsNullOrEmpty(current.Name) ? "Last Session" : current.Name;
                    list.Insert(0, current);
                }
                
                return list;
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        private static async Task<ObservableCollection<SavedSession>> LoadSessionsInternal()
        {
            var list = new ObservableCollection<SavedSession>();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FILE_NAME);
                var text = await FileIO.ReadTextAsync(file);
                
                using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
                {
                    var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(List<SavedSession>));
                    var loaded = (List<SavedSession>)serializer.ReadObject(stream);
                    foreach (var s in loaded) list.Add(s);
                }
            }
            catch (Exception) { }

            return list;
        }

        private static async Task WriteSessionsInternal(IEnumerable<SavedSession> sessions)
        {
             var list = new List<SavedSession>(sessions);
             using (var stream = new System.IO.MemoryStream())
             {
                 var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(List<SavedSession>));
                 serializer.WriteObject(stream, list);
                 stream.Position = 0;
                 using (var reader = new System.IO.StreamReader(stream))
                 {
                     var text = reader.ReadToEnd();
                     var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FILE_NAME, CreationCollisionOption.ReplaceExisting);
                     await FileIO.WriteTextAsync(file, text);
                 }
             }
        }
        
        public static async Task DeleteSession(SavedSession session)
        {
            await _sessionSemaphore.WaitAsync();
            try
            {
                var sessions = await LoadSessionsInternal();
                var target = sessions.FirstOrDefault(s => s.Name == session.Name);
                if (target != null)
                {
                    sessions.Remove(target);
                    await WriteSessionsInternal(sessions);
                }
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }
        public static async Task ArchiveSession(ObservableCollection<SubsonicItem> queue, string name, Guid sessionId, int index, TimeSpan position, bool isShuffle, int repeatMode)
        {
            if (queue == null || queue.Count == 0) return;
            
            await _sessionSemaphore.WaitAsync();
            try
            {
                var sessions = await LoadSessionsInternal();
                var existing = sessions.FirstOrDefault(s => s.Id == sessionId);
                
                if (existing != null)
                {
                    // Update existing
                    sessions.Remove(existing);
                    existing.Created = DateTime.Now;
                    existing.Tracks = new List<SubsonicItem>(queue);
                    existing.CurrentIndex = index;
                    existing.Position = position;
                    existing.IsShuffle = isShuffle;
                    existing.RepeatMode = repeatMode;
                    existing.Name = name; // Update name in case it changed?
                    sessions.Insert(0, existing);
                }
                else
                {
                    // Create new
                    var newSession = new SavedSession 
                    { 
                        Id = sessionId,
                        Name = name, 
                        Created = DateTime.Now, 
                        Tracks = new List<SubsonicItem>(queue),
                        CurrentIndex = index,
                        Position = position,
                        IsShuffle = isShuffle,
                        RepeatMode = repeatMode
                    };
                    sessions.Insert(0, newSession);
                }
                
                await WriteSessionsInternal(sessions);
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }
        public static async Task RenameSession(SavedSession session, string newName)
        {
            await _sessionSemaphore.WaitAsync();
            try
            {
                var sessions = await LoadSessionsInternal();
                var target = sessions.FirstOrDefault(s => s.Id == session.Id);
                // Fallback to name match if ID mismatch (legacy)
                if (target == null) target = sessions.FirstOrDefault(s => s.Name == session.Name);
                
                if (target != null)
                {
                    target.Name = newName;
                    await WriteSessionsInternal(sessions);
                }
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }
    }

    [System.Runtime.Serialization.DataContract]
    public class SavedSession
    {
        [System.Runtime.Serialization.DataMember]
        public string Name { get; set; }
        
        [System.Runtime.Serialization.DataMember]
        public DateTime Created { get; set; }
        
        [System.Runtime.Serialization.DataMember]
        public List<SubsonicItem> Tracks { get; set; }

        [System.Runtime.Serialization.DataMember]
        public int CurrentIndex { get; set; }

        [System.Runtime.Serialization.DataMember]
        public Guid Id { get; set; } = Guid.NewGuid();

        [System.Runtime.Serialization.DataMember]
        public TimeSpan Position { get; set; }

        [System.Runtime.Serialization.DataMember]
        public bool IsShuffle { get; set; } // Add IsShuffle

        [System.Runtime.Serialization.DataMember]
        public int RepeatMode { get; set; } // Add RepeatMode (0=off, 1=all, 2=one)

        public int TrackCount => Tracks?.Count ?? 0;
        public string CreatedFormatted => Created.ToString("dd.MM.yyyy HH:mm:ss");
    }
}
