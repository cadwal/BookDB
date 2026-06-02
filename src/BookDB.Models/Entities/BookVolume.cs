using System.Collections.Generic;

namespace BookDB.Models.Entities;

public class BookVolume
{
    public int BookVolumeId { get; set; }
    public int BookId { get; set; }
    public int VolumeNumber { get; set; }
    public Book? Book { get; set; }
    public ICollection<BookChapter> Chapters { get; set; } = [];
}
