namespace DoggyDrop.ViewModels
{
    public class UserProfileViewModel
    {
        public string Email { get; set; } = string.Empty;
        public int TotalBins { get; set; }
        public List<string> Badges { get; set; } = new();
    }
}
