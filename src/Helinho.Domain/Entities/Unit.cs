namespace Helinho.Domain.Entities;

public class Unit
{
    private Unit()
    {
    }

    public Unit(string name)
    {
        Name = Guard.Required(name, "Nome da unidade");
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; } = "";
}
