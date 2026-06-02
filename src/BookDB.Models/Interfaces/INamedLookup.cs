namespace BookDB.Models.Interfaces;

public interface INamedLookup
{
    int Id { get; }
    string Name { get; set; }
}
