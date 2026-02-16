using System;
using System.ComponentModel;
using System.Runtime.Serialization;
using Windows.UI.Xaml.Media;

namespace SubsonicUWP
{
    [DataContract]
    public class SubsonicItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }
        
        [DataMember]
        public string Id { get; set; }
        [DataMember]
        public string Title { get; set; }
        [DataMember]
        public int TrackNumber { get; set; }
        [DataMember]
        public string Artist { get; set; }
        [DataMember]
        public string Suffix { get; set; }
        [DataMember(Name = "artistId")]
        public string ArtistId { get; set; }
        
        private string _coverArtId;
        [DataMember]
        public string CoverArtId 
        { 
            get => _coverArtId;
            set
            {
                if (_coverArtId != value)
                {
                    _coverArtId = value;
                     // Reset cached image url if ID changes
                    _cachedImageUrl = null;
                }
            }
        }
        
        [DataMember]
        public string Album { get; set; }
        [DataMember(Name = "albumId")]
        public string AlbumId { get; set; }
        [DataMember]
        public DateTime? Created { get; set; }
        
        [IgnoreDataMember]
        public SolidColorBrush ColorBrush { get; set; } // Fallback

        // Helpers
        private string _cachedImageUrl;
        public string ImageUrl 
        {
            get
            {
                if (_cachedImageUrl == null)
                {
                    _cachedImageUrl = SubsonicService.Instance.GetCoverArtUrl(CoverArtId);
                }
                return _cachedImageUrl;
            }
        }
        
        // We'll calculate StreamUrl on demand usually, but having a property helps binding if needed
        public string StreamUrl => SubsonicService.Instance.GetStreamUrl(Id);
        
        [DataMember]
        public int Duration { get; set; }
        public string DurationFormatted => TimeSpan.FromSeconds(Duration).ToString(@"mm\:ss");

        [DataMember]
        public bool IsStarred { get; set; }
        public SubsonicItem Clone()
        {
            var clone = (SubsonicItem)this.MemberwiseClone();
            clone.IsPlaying = false; // Reset state for clone
            return clone;
        }
    }
}
