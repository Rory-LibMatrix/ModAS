using Microsoft.AspNetCore.Http.Headers;

namespace MxApiExtensions.Extensions;

public static class RequestHeaderExtensions {
    public static bool TryGet<T>(this RequestHeaders headers, string name, out T? value) {
        try {
            value = headers.Get<T>(name);
            return true;
        }
        catch (Exception) {
            value = default;
        }

        return false;
    }
}