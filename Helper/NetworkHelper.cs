using System.Net.Http;
using System.Threading.Tasks;

namespace True.Helper
{
    public static class NetworkHelper
    {
        public static async Task<string> GetPublicIP()
        {
            try
            {
                using var client = new HttpClient();
                return await client.GetStringAsync("https://api.ipify.org");
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
