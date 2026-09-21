namespace DoggyDrop.ViewModels;

public static class SlovenianFormatting
{
    public static string Days(int days) => days == 1 ? "1 dan" : $"{days} dni";
}
