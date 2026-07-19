namespace Sample.Web;

public sealed class OrderRepository
{
    public string SelectById() => "SELECT Id, Number FROM dbo.Orders WHERE Id = @id";
}
