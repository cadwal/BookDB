using System.Threading.Tasks;

namespace BookDB.Desktop.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
