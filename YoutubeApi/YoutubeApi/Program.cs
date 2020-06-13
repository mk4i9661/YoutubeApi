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
        static void Main(string[] args)
        {
            try
            {
                new Program().Run(args);
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.Error.WriteLine("ERROR: " + e.Message);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
            }
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        class ServiceCollection
        {
            private String[] SecretsCollection
            {
                get;
                set;
            }

            private int currentServiceIndex;
            private YouTubeService service;

            public ServiceCollection(string[] secretsCollection)
            {
                SecretsCollection = secretsCollection;
                currentServiceIndex = 0;
            }

            private YouTubeService CreateService()
            {
                if (currentServiceIndex + 1 > SecretsCollection.Length)
                {
                    throw new Exception("No more API keys left.");
                }
                var fileSecret = new FileInfo(SecretsCollection[currentServiceIndex++]);
                using (var stream = fileSecret.OpenRead())
                {
                    var credPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                        string.Format(".credentials/{0}", fileSecret.Name)
                        );

                    var credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.Load(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(credPath, true)).Result;
                    Console.WriteLine("Credential file saved to: " + credPath);

                    return new YouTubeService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "YoutubeApiTest"
                    });
                }
            }

            public void ChooseNextService()
            {
                service = CreateService();
            }

            public YouTubeService CurrentService
            {
                get
                {
                    if (service == null)
                    {
                        ChooseNextService();
                    }
                    return service;
                }
            }
        }

        private T Retry<T>(ServiceCollection collection, Func<YouTubeService, T> requestToMake)
        {
            try
            {
                return requestToMake(collection.CurrentService);
            }
            catch (Google.GoogleApiException exception)
            {
                var youtubeError = exception.Error.Errors.FirstOrDefault();
                if (youtubeError != null)
                {
                    if (string.Equals("youtube.quota", youtubeError.Domain, StringComparison.InvariantCultureIgnoreCase))
                    {
                        Console.Error.WriteLine("It seems that the request quota has been met. Trying another API key...");
                        collection.ChooseNextService();
                        return Retry(collection, requestToMake);
                    }
                }
                throw;
            }
        }

        IEnumerable<Playlist> GetMinePlaylists(ServiceCollection collection)
        {
            var result = new List<Playlist>();

            string token = "";
            while (token != null)
            {
                var requestResult = Retry(collection, service =>
                {
                    var request = service.Playlists.List("id,snippet");
                    request.Mine = true;
                    request.PageToken = token;
                    request.MaxResults = 50;
                    return request.Execute();
                });
                token = requestResult.NextPageToken;
                result.AddRange(requestResult.Items);
            }

            return result;
        }

        IEnumerable<PlaylistItem> GetPlaylistItems(ServiceCollection collection, Playlist playlist)
        {
            var result = new List<PlaylistItem>();

            string token = "";
            while (token != null)
            {
                var requetResult = Retry(collection, service =>
                {
                    var request = service.PlaylistItems.List("id,snippet,contentDetails");
                    request.PlaylistId = playlist.Id;
                    request.PageToken = token;
                    request.MaxResults = 50;
                    return request.Execute();
                });
                token = requetResult.NextPageToken;
                result.AddRange(requetResult.Items);
            }

            return result;
        }

        void UpdateNote(ServiceCollection collection, PlaylistItem item)
        {
            var updatedItem = new PlaylistItem();
            updatedItem.Id = item.Id;
            updatedItem.Snippet = new PlaylistItemSnippet()
            {
                PlaylistId = item.Snippet.PlaylistId,
                ResourceId = item.Snippet.ResourceId,
            };

            updatedItem.ContentDetails = new PlaylistItemContentDetails()
            {
                Note = item.Snippet.Title
            };

            try
            {
                Retry(collection, service =>
                {
                    var request = service.PlaylistItems.Update(updatedItem, "id,snippet,contentDetails");
                    request.Fields = "id,snippet/playlistId,snippet/resourceId,contentDetails/note";
                    return request.Execute();
                });
            }
            catch (Google.GoogleApiException exception)
            {
                Console.Error.WriteLine("An error occured while processsing playlist item. Id: {0}. Exception: {1}{2}", item.Id, Environment.NewLine, exception.Message);
            }
        }

        private void Run(string[] credentialsToUse)
        {
            var collection = new ServiceCollection(credentialsToUse);

            var playlists = GetMinePlaylists(collection).ToArray();


            Console.WriteLine("Total playlist to process: {0}", playlists.Length);
            foreach (var playlist in playlists)
            {
                Console.WriteLine("Processing playlist: {0}", playlist.Snippet.Title);
                var items = GetPlaylistItems(collection, playlist).ToArray();
                Console.WriteLine("Total items: {0}", items.Length);
                foreach (var item in items)
                {
                    Console.WriteLine("Processing playlist item: {0}", item.Snippet.Title);
                    if (!string.IsNullOrEmpty(item.ContentDetails.Note))
                    {
                        continue;
                    }
                    UpdateNote(collection, item);
                }
                Console.WriteLine("__________");
            }
        }
    }
}