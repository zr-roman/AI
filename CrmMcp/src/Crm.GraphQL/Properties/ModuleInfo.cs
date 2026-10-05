using HotChocolate;

// Имя модуля для source generator Hot Chocolate. Он создаёт метод AddCrmTypes(), который регистрирует
// все [QueryType], [MutationType], [SubscriptionType], [ObjectType<T>] и [DataLoader] этой сборки.
[assembly: Module("CrmTypes")]
