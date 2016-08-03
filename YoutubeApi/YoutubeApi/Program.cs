using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace YoutubeApi
{
    class Program
    {
        static readonly string[] Scopes = { YouTubeService.Scope.Youtube };

        [STAThread]
        static void Main(string[] args) {
            try {
                new Program().Run();
            } catch (AggregateException ex) {
                foreach (var e in ex.InnerExceptions) {
                    Console.WriteLine("ERROR: " + e.Message);
                }
            }
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        IEnumerable<Playlist> GetMinePlaylists(YouTubeService service) {
            var result = new List<Playlist>();

            string token = "";
            while (token != null) {
                var request = service.Playlists.List("id,snippet");
                request.Mine = true;
                request.PageToken = token;
                request.MaxResults = 50;
                var resultResult = request.Execute();
                token = resultResult.NextPageToken;
                result.AddRange(resultResult.Items);
            }

            return result;
        }

        IEnumerable<PlaylistItem> GetPlaylistItems(YouTubeService service, Playlist playlist) {
            var result = new List<PlaylistItem>();

            string token = "";
            while (token != null) {
                var request = service.PlaylistItems.List("id,snippet,contentDetails");
                request.PlaylistId = playlist.Id;
                request.PageToken = token;
                request.MaxResults = 50;
                var resultResult = request.Execute();
                token = resultResult.NextPageToken;
                result.AddRange(resultResult.Items);
            }

            return result;
        }

        void UpdateNote(YouTubeService service, PlaylistItem item) {
            var updatedItem = new PlaylistItem();
            updatedItem.Id = item.Id;
            updatedItem.Snippet = new PlaylistItemSnippet() {
                PlaylistId = item.Snippet.PlaylistId,
                ResourceId = item.Snippet.ResourceId,
            };
            //updatedItem.Snippet

            updatedItem.ContentDetails = new PlaylistItemContentDetails() {
                Note = item.Snippet.Title
            };

            var request = service.PlaylistItems.Update(updatedItem, "id,snippet,contentDetails");
            request.Fields = "id,snippet/playlistId,snippet/resourceId,contentDetails/note";
            try {
                request.Execute();
            } catch (Exception exception) {
                Console.WriteLine("An error occured while processsing playlist item. Id: {0}. Exception: {1}{2}", item.Id, Environment.NewLine, exception.Message);
            }
        }

        private void Run() {
            UserCredential credential;


            using (var stream = new MemoryStream(Properties.Resources.client_secret)) {
                string credPath = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.Personal);
                credPath = Path.Combine(credPath, ".credentials/youtube-api-test.json");

                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("Credential file saved to: " + credPath);
            }

            var youtube = new YouTubeService(new BaseClientService.Initializer() {
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeApiTest"
            });

            var playlists = GetMinePlaylists(youtube).ToArray();


            Console.WriteLine("Total playlist to process: {0}", playlists.Length);
            foreach (var playlist in playlists) {
                Console.WriteLine("Processing playlist: {0}", playlist.Snippet.Title);
                var items = GetPlaylistItems(youtube, playlist).ToArray();
                Console.WriteLine("Total items: {0}", items.Length);
                foreach (var item in items) {
                    Console.WriteLine("Processing playlist item: {0}", item.Snippet.Title);
                    if (!string.IsNullOrEmpty(item.ContentDetails.Note)) {
                        continue;
                    }
                    UpdateNote(youtube, item);
                }
                Console.WriteLine("__________");
            }
        }
    }
}