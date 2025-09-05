using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Cloak.Services.Profile
{
    public sealed class FileProfileService : IProfileService
    {
        private readonly string _root;

        public FileProfileService(string root)
        {
            _root = root;
        }

        public async Task<string> GetProfileContextAsync()
        {
            var sb = new StringBuilder();
            async Task AppendAsync(string fileName, string heading)
            {
                var path = Path.Combine(_root, fileName);
                if (File.Exists(path))
                {
                    sb.AppendLine($"[{heading}]");
                    sb.AppendLine(await File.ReadAllTextAsync(path));
                    sb.AppendLine();
                }
            }

            await AppendAsync("resume.txt", "Resume");
            await AppendAsync("job_description.txt", "Job Description");
            await AppendAsync("additional_details.txt", "Additional Details");
            return sb.ToString();
        }
    }
}


