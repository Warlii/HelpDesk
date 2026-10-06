# Requires PowerShell 7 and Docker Desktop. Uses an isolated, disposable Compose project.
$ErrorActionPreference = 'Stop'
$project = 'helpdesk-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$names = 'POSTGRES_PASSWORD', 'JWT_KEY', 'API_PORT', 'DB_PORT'
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function Free-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}
$env:POSTGRES_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:JWT_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$env:API_PORT = Free-Port
$env:DB_PORT = Free-Port
$base = "http://127.0.0.1:$env:API_PORT"
$script:checks = 0
function Request($method, $path, $body, $token, $expected) {
    $options = @{ Method=$method; Uri="$base$path"; SkipHttpErrorCheck=$true; TimeoutSec=10 }
    if ($null -ne $body) { $options.Body = $body | ConvertTo-Json -Depth 10; $options.ContentType='application/json' }
    if ($token) { $options.Headers = @{Authorization="Bearer $token"} }
    $response = Invoke-WebRequest @options
    if ([int]$response.StatusCode -ne $expected) { throw "$method $path expected $expected, received $($response.StatusCode): $($response.Content)" }
    $script:checks++
    if ($expected -ge 400) {
        if ($response.Headers['Content-Type'] -notmatch 'application/problem\+json') { throw "Expected ProblemDetails for $path" }
    }
    if ($response.Content) { return $response.Content | ConvertFrom-Json }
}
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    docker compose -p $project up --build -d
    if ($LASTEXITCODE -ne 0) { throw 'Compose startup failed.' }
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        try { $r = Invoke-WebRequest "$base/swagger/v1/swagger.json" -SkipHttpErrorCheck; if($r.StatusCode -eq 200) {$ready=$true;break} } catch {}
        Start-Sleep -Seconds 1
    }
    if (!$ready) { docker compose -p $project logs; throw 'API did not become ready.' }
    $password = 'Long-test-password-123!'
    $user = Request POST /api/auth/register @{name='Owner';email='owner@example.test';password=$password} $null 201
    $other = Request POST /api/auth/register @{name='Other';email='other@example.test';password=$password} $null 201
    $admin = Request POST /api/auth/register @{name='Admin';email='admin@example.test';password=$password;role='Admin'} $null 201
    if($admin.role -ne 'User') { throw 'Registration allowed privilege escalation.' }
    # Promote only this disposable database account through an operator action.
    $sql = 'UPDATE "Users" SET "Role"=1 WHERE "Email"=''admin@example.test'';'
    $sql | docker compose -p $project exec -T db psql -U helpdesk -d helpdesk -v ON_ERROR_STOP=1
    if($LASTEXITCODE -ne 0) {throw 'Admin setup failed.'}
    $ownerToken = (Request POST /api/auth/login @{email='owner@example.test';password=$password} $null 200).accessToken
    $otherToken = (Request POST /api/auth/login @{email='other@example.test';password=$password} $null 200).accessToken
    $adminToken = (Request POST /api/auth/login @{email='admin@example.test';password=$password} $null 200).accessToken
    $null = Request POST /api/auth/login @{email='owner@example.test';password='incorrect'} $null 401
    $null = Request POST /api/auth/register @{name='Owner';email='OWNER@example.test';password=$password} $null 409
    $null = Request POST /api/auth/register @{name='X';email='bad';password='short'} $null 400
    $null = Request GET /api/tickets $null $null 401
    $null = Request GET /api/tickets $null 'invalid-token' 401
    $null = Request GET /api/users $null $ownerToken 403
    $null = Request GET /api/users $null $adminToken 200
    $null = Request GET /api/users/me $null $ownerToken 200
    $ticket = Request POST /api/tickets @{title='Printer failure';description='The office printer stopped working.';priority='High'} $ownerToken 201
    if($ticket.PSObject.Properties.Name -contains 'passwordHash') {throw 'Sensitive field exposed.'}
    $id=$ticket.id
    $null = Request GET "/api/tickets/$id" $null $ownerToken 200
    $null = Request GET "/api/tickets/$id" $null $otherToken 403
    $others = @(Request GET /api/tickets $null $otherToken 200)
    if($others.Count -ne 0) {throw 'Ticket list leaked another user data.'}
    $null = Request POST "/api/tickets/$id/comments" @{content='Forbidden'} $otherToken 403
    $null = Request PATCH "/api/tickets/$id/status" @{status='InProgress'} $ownerToken 403
    $null = Request PATCH "/api/tickets/$id/assignment" @{userId=[Guid]::NewGuid().ToString()} $adminToken 404
    $null = Request PATCH "/api/tickets/$id/assignment" @{userId=$other.id} $adminToken 204
    $null = Request GET "/api/tickets/$id" $null $otherToken 403
    $null = Request PATCH "/api/tickets/$id/priority" @{priority='Critical'} $adminToken 204
    $null = Request POST "/api/tickets/$id/comments" @{content='Additional information'} $ownerToken 201
    $null = Request POST "/api/tickets/$id/comments" @{content='Investigating'} $adminToken 201
    $comments = @(Request GET "/api/tickets/$id/comments" $null $ownerToken 200)
    if($comments.Count -ne 2) {throw 'Comments not persisted.'}
    $filtered = @(Request GET "/api/tickets?status=Open&priority=Critical&assignedToUserId=$($other.id)&from=2020-01-01T00%3A00%3A00Z&to=2100-01-01T00%3A00%3A00Z" $null $adminToken 200)
    if($filtered.Count -ne 1) {throw 'Filters failed.'}
    $null = Request GET "/api/tickets?from=2100-01-01&to=2020-01-01" $null $ownerToken 400
    $null = Request GET "/api/tickets?status=Invalid" $null $ownerToken 400
    $null = Request GET "/api/tickets?page=0" $null $ownerToken 400
    $null = Request GET "/api/tickets/$([Guid]::NewGuid())" $null $ownerToken 404
    $null = Request POST /api/tickets @{title=' ';description='short';priority='Low'} $ownerToken 400
    $null = Request POST /api/tickets @{title='Valid title';description='Valid description';priority=999} $ownerToken 400
    $null = Request PATCH "/api/tickets/$id/status" @{} $adminToken 400
    $null = Request PATCH "/api/tickets/$id/priority" @{} $adminToken 400
    $null = Request PATCH "/api/tickets/$id/assignment" @{} $adminToken 400
    $null = Request PATCH "/api/tickets/$id/status" @{status='Resolved'} $adminToken 409
    $null = Request PATCH "/api/tickets/$id/status" @{status='Invalid'} $adminToken 400
    foreach($status in 'InProgress','Resolved','Closed') {
        $null = Request PATCH "/api/tickets/$id/status" @{status=$status} $adminToken 204
    }
    $null = Request PATCH "/api/tickets/$id/status" @{status='InProgress'} $adminToken 409
    $null = Request POST "/api/tickets/$id/comments" @{content='Closed comment'} $ownerToken 409
    $final = Request GET "/api/tickets/$id" $null $adminToken 200
    if($final.status -ne 'Closed') {throw 'Final state incorrect.'}
    # Verify referential integrity against actual PostgreSQL.
    $delete = 'DELETE FROM "Users" WHERE "Email"=''owner@example.test'';'
    $delete | docker compose -p $project exec -T db psql -U helpdesk -d helpdesk -v ON_ERROR_STOP=1 2>$null
    if($LASTEXITCODE -eq 0) {throw 'Database allowed deletion of referenced user.'}
    Write-Output "Passed $script:checks HTTP checks, migrations and PostgreSQL foreign-key protection."
}
finally {
    # Only the randomly named disposable project and its volume are removed.
    docker compose -p $project down -v --remove-orphans
    Pop-Location
    foreach($name in $names) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') }
}



