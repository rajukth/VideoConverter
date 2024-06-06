using Microsoft.AspNetCore.Http;

namespace VideoConverter.Models
{
    public class UploadFileVm
    {
        public List<IFormFile> Files { get; set; }
        public IFormFile File { get; set; }
        public string FileName { get; set; }
    }
}
