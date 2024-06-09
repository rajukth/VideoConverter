namespace VideoConverter.Models
{
    public class VideoProcessingModel
    {
        public List<IFormFile> Files { get; set; }
        public string Action { get; set; }
        public string TaskId { get; set; }=Guid.NewGuid().ToString();
    }
}
