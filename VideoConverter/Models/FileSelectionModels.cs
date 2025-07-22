namespace VideoConverter.Models
{
    public class FileSelectionModel
    {
        public List<string> FileNames { get; set; }
        public string Action { get; set; }  // "ConvertOnly", "ConvertAndMerge", "MergeOnly"
    }
}
