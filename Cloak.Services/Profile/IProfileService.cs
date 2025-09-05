using System.Threading.Tasks;

namespace Cloak.Services.Profile
{
    public interface IProfileService
    {
        Task<string> GetProfileContextAsync();
    }
}


