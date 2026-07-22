using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Desktop.Localization;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The edit form's contributor rows after adopting the shared person type-ahead: new rows
/// default to the Author role, the suggestion list offers reuse plus a localized "new author"
/// row only when the text matches nobody, and the per-row "new" badge renders exactly while
/// the typed name resolves to no existing person (both states asserted via the resource text).
/// </summary>
public class ContributorSuggestionFlowTests : HeadlessTest
{
    [Fact]
    public async Task ContributorRow_DefaultsToAuthorRole_AndFlagsNewNamesUntilTheyMatch()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create();
            var bookId = await EditSample.SeedAsync(host, ct);

            var vm = host.Resolve<FullDetailsWindowViewModel>();
            Assert.True(await vm.LoadBookAsync(bookId));
            var window = new FullDetailsWindow { DataContext = vm };
            window.Show();
            Ui.Pump();

            // Render the Contributors tab (lazy TabControl content).
            var tabControl = window.Find<TabControl>();
            tabControl.SelectedItem = tabControl.Items.OfType<TabItem>()
                .First(t => Equals(t.Header, Resources.Tab_ContributorsAdmin));
            Ui.Pump();

            // A new row defaults to the Author role — a silent null role would drop the name on save.
            vm.AddContributorCommand.Execute(null);
            var row = vm.Contributors.Last();
            var authorRoleId = vm.ContributorRoles.First(r => r.Code == "Author").ContributorRoleId;
            Assert.Equal(authorRoleId, row.RoleId);

            // Unknown name: the row resolves to nobody and its "new" badge renders.
            row.SearchText = "Zzz Nobody";
            Ui.Pump();
            Assert.True(row.IsNewPerson);
            var badges = window.Descendants<TextBlock>()
                .Where(t => t.Text == Resources.PersonSuggestion_NewBadge)
                .ToList();
            Assert.Contains(badges, b => b.IsEffectivelyVisible);

            // Correcting to the seeded person turns the row into a reuse and hides every badge.
            row.SearchText = "Orig Author";
            Ui.Pump();
            Assert.True(row.IsExistingPerson);
            Assert.DoesNotContain(
                window.Descendants<TextBlock>().Where(t => t.Text == Resources.PersonSuggestion_NewBadge),
                b => b.IsEffectivelyVisible);

            // The dropdown source: a partial match offers reuse plus the localized "new author" row;
            // an exact match suppresses the new-author row.
            var partial = (await row.Populator("Orig", CancellationToken.None))!
                .Cast<IPersonSuggestion>().ToList();
            Assert.Contains(partial, s => s is ExistingPersonSuggestion e && e.Person.DisplayName == "Orig Author");
            Assert.Contains(partial, s =>
                s is NewPersonSuggestion n &&
                n.DisplayText == string.Format(Resources.PersonSuggestion_NewAuthor_Format, "Orig"));

            var exact = (await row.Populator("Orig Author", CancellationToken.None))!
                .Cast<IPersonSuggestion>().ToList();
            Assert.DoesNotContain(exact, s => s is NewPersonSuggestion);

            window.Close();
        });
    }
}
