using BukStatus.Core.Pot;
using BukStatus.Core.Transactions;
namespace BukStatus.Core.Tests;
public class TransactionSearchTests
{
    readonly ExtraContribution[] rows = [new(1,new(2026,11,1),1200.5m,"Dugnad BUK","Synthetic Alice",123,4),new(2,new(2026,10,31),-100,"Retur","Synthetic Bob",124)];
    [Theory]
    [InlineData("alice dugnad", 1)]
    [InlineData("1200,5", 1)]
    [InlineData("1200.5", 1)]
    [InlineData("2026-11-01", 1)]
    [InlineData("31.10.2026", 1)]
    [InlineData("124", 1)]
    [InlineData("Alice Retur", 0)]
    [InlineData("", 2)]
    public void Searches_names_ids_dates_descriptions_and_amounts(string search, int count) => Assert.Equal(count, TransactionService.Search(rows, search).Count);
}
