using SampleShop.Api;
using SampleShop.Orders;

var endpoints = new OrderEndpoints(new OrderService(new InMemoryInventory(), new FakePaymentGateway()));
var lines = new List<OrderLine> { new("SKU-1", 2, 10m) };

Console.WriteLine(await endpoints.CreateOrderAsync("customer-1", lines, CancellationToken.None));
Console.WriteLine(endpoints.PreviewDiscount(lines));
