using Microsoft.AspNetCore.Mvc;

namespace Sample.Web;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _service;

    public OrdersController(IOrderService service) => _service = service;

    [HttpGet("{id:int}")]
    public string Get(int id) => _service.Get(id);
}

public interface IOrderService
{
    string Get(int id);
}

public sealed class OrderService : IOrderService
{
    public string Get(int id)
    {
        if (id <= 0) return "invalid";
        return $"order-{id}";
    }
}
