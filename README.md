# HelpDesk.Api

API REST de help desk para portfólio, com C# / ASP.NET Core .NET 10, controllers, EF Core, PostgreSQL, autenticação JWT e Swagger com botão **Authorize**.

## Arquitetura

- `Controllers`: contratos HTTP e autorização declarativa.
- `Services`: autenticação, acesso e regras do ciclo de vida.
- `Data` e `Migrations`: DbContext, constraints, relacionamentos e evolução do banco.
- `Models`: entidades e enums; `DTOs`: entradas e respostas públicas.
- `Infrastructure`: exceções de negócio e ProblemDetails.
- `HelpDesk.Api.Tests`: testes xUnit de negócio; `scripts/Smoke-Test.ps1`: integração HTTP com PostgreSQL real.

Controllers usam serviços diretamente e estes usam EF Core. Não há repository genérico, CQRS ou Identity completo. O hashing usa `PasswordHasher<User>` do ASP.NET Core, com salt e suporte a rehash.

## Regras de negócio

Registro sempre cria role **User**. O cliente não escolhe role.

Usuários comuns criam, listam e consultam exclusivamente seus próprios tickets e comentam neles. Admins acessam todos, atribuem a usuários existentes, alteram prioridade e status. A atribuição identifica o responsável e não concede acesso extra a um usuário comum.

O único fluxo permitido é:

```text
Open -> InProgress -> Resolved -> Closed
```

Repetir status, pular etapas ou reabrir retorna **409**. Enum inválido retorna **400**. Tickets fechados não aceitam comentários, atribuições ou alterações de prioridade. Comentários e tickets não possuem endpoints de exclusão. Foreign keys com `Restrict` preservam usuários e registros referenciados. Atualizações concorrentes de tickets usam `xmin` do PostgreSQL e retornam **409** quando há conflito.

## Executar com Docker

Requisitos: Docker com Compose. Não precisa instalar SDK no host.

1. Copie `.env.example` para `.env`.
2. Substitua os placeholders por valores aleatórios. A chave JWT precisa de pelo menos 32 bytes.
3. Execute:

```powershell
Copy-Item .env.example .env
# Edite .env antes de continuar.
docker compose up --build -d
docker compose logs migrate api
```

Swagger: [http://localhost:8080/swagger](http://localhost:8080/swagger).

O banco tem healthcheck; o serviço `migrate` aplica migrations antes de iniciar a API. O volume `pgdata` persiste dados. Portas são vinculadas a localhost. `DB_PORT` e `API_PORT` podem ser ajustados no arquivo de ambiente.

```powershell
docker compose down
```

Use `docker compose down -v` somente se desejar apagar o banco local. O Compose é voltado a desenvolvimento e habilita Swagger. Para publicar, configure `ASPNETCORE_ENVIRONMENT=Production`, gerencie secrets externamente, use TLS no proxy e aplique migrations como uma etapa controlada de deploy.

## Executar localmente

Requisitos: SDK .NET 10 e PostgreSQL. Todas as instruções partem da raiz da solução.

```powershell
dotnet restore
dotnet tool restore
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=helpdesk;Username=helpdesk;Password=<senha-local>"
$env:Jwt__Key = "<chave-aleatoria-com-pelo-menos-32-bytes>"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project HelpDesk.Api
dotnet run --project HelpDesk.Api --urls http://localhost:8080
```

Variáveis usam `__` para níveis de configuração. `Jwt__Issuer`, `Jwt__Audience` e `Jwt__Minutes` são opcionais; os defaults estão em `appsettings.json`. A aplicação falha cedo se banco ou chave não estiverem configurados. Não registre passwords/tokens em logs ou cole secrets em arquivos versionados. `.env`, configurações locais e artefatos de build estão no `.gitignore`.

Para executar somente o banco via Compose, configure `.env` e use `docker compose up -d db`.

### Migrations

A migration `InitialCreate` está versionada com seu snapshot. Para alterações futuras:

```powershell
dotnet ef migrations add NomeDaAlteracao --project HelpDesk.Api
dotnet ef database update --project HelpDesk.Api
```

Também é possível executar `dotnet run --project HelpDesk.Api -- --migrate`, que aplica migrations e termina sem iniciar o servidor. Este modo exige as mesmas variáveis de configuração.

## Autenticação e administração

Registre uma conta, faça login e envie `Authorization: Bearer <accessToken>`. Na interface Swagger, clique **Authorize** e cole somente o token.

Senhas têm entre 12 e 128 caracteres. Emails são normalizados para minúsculas e têm índice único. Os tokens expiram em 60 minutos e validam assinatura, issuer, audience e validade. Não há refresh token ou revogação imediata; mudanças de role exigem novo login e tokens antigos permanecem válidos até expirar. Limitação de 20 requisições/minuto por IP protege os endpoints de autenticação (429). É local ao processo.

Para criar o primeiro administrador, registre uma conta e promova-a por uma ação explícita do operador no banco. Não existe endpoint público de promoção:

```sql
UPDATE "Users" SET "Role" = 1 WHERE "Email" = 'seu-email@example.com';
```

No Docker, use `docker compose exec db psql -U helpdesk -d helpdesk` e execute o SQL. Depois faça login novamente.

## Endpoints

| Método | Rota | Acesso / resposta |
|---|---|---|
| POST | /api/auth/register | Público, 201 |
| POST | /api/auth/login | Público, 200 com JWT |
| GET | /api/users/me | Autenticado, 200 |
| GET | /api/users?page=1 | Admin, 200, páginas de 50 |
| POST | /api/tickets | Autenticado, 201 e Location |
| GET | /api/tickets | Próprios ou todos para Admin, 200 |
| GET | /api/tickets/{id} | Dono ou Admin, 200 |
| PATCH | /api/tickets/{id}/assignment | Admin, 204 |
| PATCH | /api/tickets/{id}/priority | Admin, 204 |
| PATCH | /api/tickets/{id}/status | Admin, 204 |
| POST | /api/tickets/{id}/comments | Dono ou Admin, 201 |
| GET | /api/tickets/{id}/comments?page=1 | Dono ou Admin, 200, páginas de 50 |

Filtros de tickets: `status`, `priority`, `assignedToUserId`, `from`, `to`, `page`, `pageSize`. Datas limitam **CreatedAt**, com limites inclusivos, convertidos para UTC. Use ISO 8601 com timezone. Página padrão 1, tamanho padrão 20 e máximo 100; ordenação por criação decrescente e Id para desempate. Intervalos invertidos e enum inválido retornam 400. Os filtros nunca ampliam o acesso do usuário.

Enums em JSON são strings: `Open/InProgress/Resolved/Closed`, `Low/Medium/High/Critical`, `User/Admin`. Timestamps são `DateTimeOffset` em UTC.

Erros usam `application/problem+json`: 400 para validação, 401 para falta de autenticação ou credenciais inválidas, 403 para falta de acesso, 404 para recursos/referências inexistentes, 409 para conflitos de negócio/concor­rência/duplicidade, e 500 genérico para falhas inesperadas. Logs não incluem payloads de autenticação, tokens ou detalhes de exceptions que possam conter valores sensíveis.

## Exemplos básicos (PowerShell 7)

```powershell
$base = "http://localhost:8080"
$credentials = @{ email="user@example.com"; password="<senha-com-12-ou-mais-caracteres>" }
$registration = @{ name="Example User"; email=$credentials.email; password=$credentials.password }
Invoke-RestMethod "$base/api/auth/register" -Method Post -ContentType application/json -Body ($registration | ConvertTo-Json)
$login = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType application/json -Body ($credentials | ConvertTo-Json)
$headers = @{ Authorization="Bearer $($login.accessToken)" }
$body = @{ title="Impressora indisponível"; description="A impressora do escritório não está funcionando."; priority="High" }
$ticket = Invoke-RestMethod "$base/api/tickets" -Method Post -Headers $headers -ContentType application/json -Body ($body | ConvertTo-Json)
Invoke-RestMethod "$base/api/tickets?status=Open&priority=High" -Headers $headers
Invoke-RestMethod "$base/api/tickets/$($ticket.id)/comments" -Method Post -Headers $headers -ContentType application/json -Body '{"content":"Informação adicional sobre o problema."}'
# Com headers de um administrador:
# PATCH /api/tickets/{id}/assignment   {"userId":"UUID"}
# PATCH /api/tickets/{id}/status       {"status":"InProgress"}
```

## Testes e validação

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
pwsh -File scripts/Smoke-Test.ps1
```

Os testes xUnit cobrem a matriz de transições, acesso, criação, atribuição, comentários, filtros, validações e hashing/login. Usam EF InMemory para isolar regras; constraints, migrations e HTTP são exercitados pelo script contra PostgreSQL real.

O script cria um projeto Compose com nome aleatório, portas livres e secrets efêmeros. Verifica registro/login, tentativa de promoção via payload, respostas 200/201/204/400/401/403/404/409, atribuição, comentários, filtros, lifecycle e integridade referencial. O bloco `finally` remove containers, rede e volume apenas desse ambiente. Não usa nem apaga o banco normal de desenvolvimento.

## Decisões e trade-offs

- Autorização também fica no serviço, para não depender somente de atributos de controller.
- DTOs evitam exposição de PasswordHash, entidades rastreadas e metadados do banco.
- API pequena, sem frontend e sem abstrações que escondam EF Core.
- Consultas assíncronas recebem CancellationToken; leituras usam AsNoTracking; paginação limita volumes de resposta.
- Roles são parte do JWT. Não há administração completa de contas, recuperação de senha, auditoria persistente ou refresh tokens no escopo.
- Atribuição não muda status automaticamente. Cada transição é explícita.
- Uma operação de SaveChanges mantém comentários e UpdatedAt do ticket na mesma transação; controle otimista evita sobrescritas silenciosas.
- Não há exclusão pública nem edição do conteúdo de tickets/comentários; retenção e histórico são preservados.
- Não há Redis, mensageria, Kubernetes, MediatR ou microservices.

