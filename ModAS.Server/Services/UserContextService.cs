// using System.Collections.Concurrent;
// using System.Text.Json.Serialization;
// using ArcaneLibs.Extensions;
// using LibMatrix.Homeservers;
// using MxApiExtensions.Services;
//
// namespace ModAS.Server.Services;
//
// public class UserContextService(ModASConfiguration config, AuthenticatedHomeserverProviderService hsProvider) {
//     internal static ConcurrentDictionary<string, UserContext> UserContextStore { get; set; } = new();
//     public readonly int SessionCount = UserContextStore.Count;
//
//     public class UserContext {
//         [JsonIgnore]
//         public AuthenticatedHomeserverGeneric Homeserver { get; set; }
//     }
//
//     private readonly SemaphoreSlim _getUserContextSemaphore = new SemaphoreSlim(1, 1);
//     public async Task<UserContext> GetCurrentUserContext() {
//         var hs = await hsProvider.GetHomeserver();
//         // await _getUserContextSemaphore.WaitAsync();
//         var ucs = await UserContextStore.GetOrCreateAsync($"{hs.WhoAmI.UserId}/{hs.WhoAmI.DeviceId}/{hs.ServerName}:{hs.AccessToken}", async x => {
//             var userContext = new UserContext() {
//                 Homeserver = hs
//             };
//             
//             return userContext;
//         }, _getUserContextSemaphore);
//         // _getUserContextSemaphore.Release();
//         return ucs;
//     }
// }
