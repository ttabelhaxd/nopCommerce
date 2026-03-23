# Deliverables

## Architecture Analysis

### How are the layers organised and what are the dependency rules between them?

O nopCommerce segue uma arquitetura em camadas clássica (Layered Architecture). As principais camadas são:
    
- **Nop.Core**: contém as entidades de domínio (ex: Customer, Order, Product), interfaces base e utilitários. Não tem dependências externas.
    
- **Nop.Data**: responsável pelo acesso a dados, repositórios e configuração do Entity Framework. Depende de Nop.Core.

- **Nop.Services**: contém a lógica de negócio (serviços como IOrderService, IProductService). Depende de Nop.Core e Nop.Data.

- **Nop.Web**: a camada de apresentação (MVC, Razor Pages, controllers). Depende de Nop.Services (e indiretamente das outras).
    
A regra de dependência é estritamente descendente: Web → Services → Data → Core.

### How does nopCommerce handle events internally — what is IEventPublisher and how is it used?

O nopCommerce implementa um mecanismo de eventos interno baseado no padrão Publisher/Subscriber. A interface IEventPublisher (no projeto Nop.Services) é o ponto central para publicar eventos. Os consumidores implementam `IConsumer<T>` e são descobertos automaticamente por reflexão (através de ITypeFinder).

Eventos típicos incluem:

- **`EntityInsertedEvent<T>`**: publicado automaticamente após uma entidade ser inserida (no repositório).

- **`EntityUpdatedEvent<T>`**: após atualização.

- **`OrderPlacedEvent`**: publicado quando um pedido é colocado (no `OrderProcessingService`).

### Where does the code make it easy to add observability, and where does it make it hard?

Onde é **facil** adicionar observabilidade:

- **Sistema de eventos (IEventPublisher)** – Eventos como `EntityInsertedEvent` são publicados automaticamente, permitindo adicionar instrumentação criando `IConsumer` sem modificar o código existente. Abordagem cirúrgica e pouco intrusiva.

- **AppStartedEvent** – Útil para medir tempo de inicialização da aplicação.

Onde é **difícil** adicionar observabilidade:

- **Eventos genéricos e de baixo nível** – Publicados ao nível do repositório, sem contexto de negócio. Para obter spans semânticos (ex: "Checkout completed"), é necessário modificar serviços monolíticos como `OrderProcessingService`, o que é arriscado e intrusivo.

- **Lógica de negócio espalhada** – Serviços com métodos longos misturam várias operações, dificultando a criação de spans granulares sem refatoração.

- **PII nos eventos** – Eventos como `EntityInsertedEvent<Customer>` contêm dados sensíveis (email, morada), exigindo um processador de redação para evitar vazamento de informações.

### What would you need to change structurally to instrument it properly — and is that change worth making?

Para obter tracing granular (ex: spans para cada etapa do checkout), seria necessário refatorar serviços como `OrderProcessingService`, extraindo responsabilidades para classes menores ou usando o padrão `Decorator`. Isso permitiria injetar instrumentação sem misturar lógica de negócio. No entanto, essa mudança não compensa no contexto do assignment porque:

- Aumenta o risco de introduzir bugs num sistema estável.
- O tempo de refatoração é grande e o benefício imediato é pequeno.
- A abordagem escolhida foi usar o sistema de eventos como ponto de instrumentação, que é uma alteração mínima e segura. Para spans mais granulares, pode-se combinar com instrumentação manual nos pontos críticos, sem refatorar.



    - 

