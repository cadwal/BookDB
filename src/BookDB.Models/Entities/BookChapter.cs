namespace BookDB.Models.Entities;

public class BookChapter
{
    public int BookChapterId { get; set; }
    public int BookVolumeId { get; set; }
    public int ChapterNumber { get; set; }
    public BookVolume? Volume { get; set; }
}
