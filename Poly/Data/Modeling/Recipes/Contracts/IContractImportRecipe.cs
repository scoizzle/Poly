namespace Poly.Data.Modeling.Recipes.Contracts;

/// <summary>
/// Imports third-party contract descriptions into canonical domain contract objects.
/// </summary>
public interface IContractImportRecipe {
    string Name { get; }
    bool CanImport(ContractImportSource source);
    ContractImportResult ImportInto(Domain domain, ContractImportSource source, ContractImportOptions? options = null);
}