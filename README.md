- Leandro Poletti de Oliveira - 1982910
- Breno Colonello - 1958402
- Caio henrique ocon - 1958689

# Navarro Estacionamento - Estacionamento Inteligente (Campus)


MVP de estacionamento inteligente para um campus com 3 setores (A, B, C), cada um com 30 vagas. O sistema usa MQTT para eventos em tempo real, HTTP (REST) para consultas e relatórios, e Postgres para persistência histórica.

## Resumo Rápido
- Serviços: Mosquitto (MQTT), Backend (.NET HTTP + ingest + MQTT), Simulador (Node.js), Postgres, PgAdmin4 (opcional para administração do DB).
- Tópicos MQTT principais:
  - Eventos de vaga: `campus/parking/sectors/<sectorId>/spots/<spotId>/events`
  - Gateway status: `campus/parking/sectors/<sectorId>/gateway/status`
  - (Opcional) recomendações: `campus/parking/recommendations`

## Pré-requisitos
- Docker & docker-compose (recomendado)
- mosquitto-clients (mosquitto_pub / mosquitto_sub) ou cliente MQTT equivalente
- curl / jq (para testar HTTP)
- .NET 10.0 SDK (para desenvolvimento local do backend)
- Node.js (para desenvolvimento local do simulador)

## Quickstart (Docker)
1. Na raiz do repositório:

   ```bash
   docker-compose up --build
   ```

2. Serviços esperados:
   - mosquitto: porta 1883 (MQTT)
   - backend: porta 80 (HTTP, mapeado para 5000 no docker-compose)
   - simulator: porta 3000 (HTTP de controle)
   - postgres: porta 5432
   - pgadmin4: porta 8080 (interface web para administração do banco)
   - backend: conecta ao broker e ingere eventos diretamente no banco

## Rodando Local (sem Docker)
- **Postgres (docker rápido):**
  ```bash
  docker run --name parking-db -e POSTGRES_DB=parking -e POSTGRES_USER=parking_user -e POSTGRES_PASSWORD=parking_pass -p 5432:5432 -d postgres:15
  ```

- **Backend (.NET):**
  ```bash
  cd backend/Backend
  dotnet restore
  export ConnectionStrings__Default="Host=localhost;Database=parking;Username=parking_user;Password=parking_pass"
  dotnet run
  ```

- **Simulador:**
  ```bash
  cd simulator
  npm install
  export MQTT_HOST=localhost
  export MQTT_PORT=1883
  export SIM_TIME_RATIO=60
  npm start
  ```

## Simulador
- Simula 90 sensores (A-01..C-30) e 3 gateways (A,B,C).
- `SIM_TIME_RATIO`: segundos simulados por segundo real (ex.: 60 = 1s real = 1min simulado).
- Observação: atualmente o simulador não usa `SIM_TIME_RATIO` para ajustar chegadas/saídas.
- `OCCUPIED_HOLD_SEC`: tempo fixo de ocupação (ainda não há variação 30 min-6h em tempo simulado).
- Modos de falha injetáveis (via HTTP do simulador):
  - `stuck_occupied` (travado como OCUPADO)
  - `stuck_free` (travado como LIVRE)
  - `flapping` (flag de flapping; não alterna automaticamente)
- Endpoints do simulador (exemplos):
  - `POST http://localhost:3000/simulator/failures` `{ "spotId":"A-07", "mode":"stuck_occupied" }`
  - `POST http://localhost:3000/simulator/reset` `{ "spotId":"A-07" }`
  - (compat) `POST http://localhost:3000/faults` `{ "spotId":"A-07", "mode":"stuck_occupied" }`

## MQTT - Formato de Evento de Vaga
Tópico:
  `campus/parking/sectors/A/spots/A-07/events`

Payload (JSON mínimo):
```json
{
  "eventId":"<uuid>",
  "ts":"2026-04-29T10:15:30.000Z",
  "sectorId":"A",
  "spotId":"A-07",
  "state":"OCCUPIED",
  "source":"sensor"
}
```

## Regras de Ingestão do Backend
- Idempotência por `eventId` (não duplicar eventos no DB).
- Atualizar estado atual em `spots` e registrar evento em `spot_events`.
- Incidentes: detectar `STUCK_OCCUPIED`, `STUCK_FREE` e `FLAPPING`.

## HTTP API (Endpoints Mínimos)
- `GET /api/v1/map`
  Retorna setores, vagas e estado atual (mapa completo).

- `GET /api/v1/sectors`
  Retorna `occupiedCount`, `freeCount`, `occupancyRate`, `lastUpdateTs` por setor.

- `GET /api/v1/sectors/:sectorId/spots`
  Lista vagas de um setor com estado e timestamps.

- `GET /api/v1/sectors/:sectorId/free-spots?limit=10`
  Lista vagas livres (limit opcional).

- `GET /api/v1/reports/turnover?sectorId=A&from=<ISO>&to=<ISO>`
  Taxa de rotatividade: número de transições FREE->OCCUPIED no período.

- `GET /api/v1/incidents?status=open`
  Lista incidentes (sensor travado, flapping, etc.).

- `GET /api/v1/recommendation?fromSector=A`
  Retorna/gera recomendação quando `occupancyRate(fromSector) >= 0.90` e grava em `recommendations_log`.

## Estrutura do Projeto
- `backend/`: Código do backend em .NET (ASP.NET Core).
  - `Controllers/`: Endpoints HTTP.
  - `Models/`: Entidades do banco.
  - `DTOs/`: Objetos de transferência de dados.
  - `Services/`: Lógica de negócio (ingestão MQTT, monitoramento de incidentes, etc.).
  - `Data/`: Contexto do Entity Framework.
- `simulator/`: Simulador em Node.js.
- `init_db/`: Scripts de inicialização do banco (init.sql).
- `mosquitto/`: Configuração do broker MQTT.
- `scripts/`: Scripts para demos e testes end-to-end.

## Inicialização do Banco de Dados
Para inicializar o esquema do banco, execute o script `init_db/init.sql` no PostgreSQL:
```bash
psql -h localhost -U parking_user -d parking -f init_db/init.sql
```
Ou via PgAdmin4 (porta 8080) após o Docker estar rodando.

## Executando Demos e Testes
- **Demo E2E (Shell):** `./scripts/e2e-demo.sh` (simula eventos e testa APIs).
- **Demo Manual (PowerShell):** `./scripts/e2e-demo-manual.ps1` (para Windows).
- **Ocupar Vaga via JS:** `node scripts/e2e-occupy-js.js`.
- **Publicar MQTT Genérico:** `node scripts/mqtt-publish.js <topic> <payload>`.
- **Inserir Evento de Incidente via MQTT:** `node scripts/insert-incident-event.js <eventId> [sectorId] [spotId] [type] [mqttHost] [mqttPort]`.
  - Tipos de incidente: `STUCK_OCCUPIED`, `STUCK_FREE`, `FLAPPING`.
  - Exemplos:
    ```bash
    node scripts/insert-incident-event.js my-event-123
    node scripts/insert-incident-event.js my-event-456 B B-15 STUCK_FREE
    node scripts/insert-incident-event.js my-event-789 C C-30 FLAPPING localhost 1883
    ```

Para executar o demo:
```bash
chmod +x scripts/e2e-demo.sh
./scripts/e2e-demo.sh
```

## Desenvolvimento e Contribuição
- Para desenvolvimento, clone o repositório e siga as instruções de "Rodando Local".
- Testes: Execute `dotnet test` no backend após restaurar dependências.
- Contribuições: Abra issues ou pull requests no repositório GitHub.

Exemplos de uso (curl)
- Ver setores:
  curl -s http://localhost:5000/api/v1/sectors | jq

- Ver mapa:
  curl -s http://localhost:5000/api/v1/map | jq

- Solicitar recomendacao:
  curl -s "http://localhost:5000/api/v1/recommendation?fromSector=A" | jq

Exemplo MQTT (publicar evento manual)
mosquitto_pub -h localhost -p 1883 -t "campus/parking/sectors/A/spots/A-07/events" -m '{"eventId":"uuid-1","ts":"2026-04-29T10:15:30.000Z","sectorId":"A","spotId":"A-07","state":"OCCUPIED","source":"sensor"}' -q 1

Estado dos requisitos (Sprint 2)
- OK: MQTT topicos/payloads, ingestao idempotente, persistencia historica, endpoints HTTP, recomendacoes >= 0.90, incidentes STUCK/FLAPPING, snapshots setoriais.
- Parcial (simulador):
  - Picos de chegada (manha/fim da tarde) ainda nao simulados.
  - Permanencia 30 min-6h em tempo simulado ainda nao implementada (tempo fixo via OCCUPIED_HOLD_SEC).
  - SIM_TIME_RATIO ainda nao influencia o comportamento.
  - Modo flapping marca a vaga, mas nao gera alternancias rapidas automaticamente.

Checklist de demonstracao
1. Subir Mosquitto + Postgres + backend + simulator (docker-compose ou local).
2. Rodar mosquitto_sub -t "campus/parking/#" -v para observar eventos.
3. Ver /api/v1/map e /api/v1/sectors atualizando em tempo real.
4. Injetar falha:
   POST /simulator/failures { "spotId":"A-07", "mode":"stuck_occupied" }
   Verificar que /api/v1/incidents?status=open contem um incidente STUCK_OCCUPIED.
5. Forcar lotacao de um setor ate occupancyRate >= 0.90 (simulador ou eventos manuais).
   GET /api/v1/recommendation?fromSector=A deve retornar recommendedSector e gravar em recommendations_log.
6. Testar idempotencia: republicar o mesmo eventId e verificar que spot_events nao duplica.

Script de demo automatizada
- Linux/macOS (bash):
  - chmod +x scripts/e2e-demo.sh
  - ./scripts/e2e-demo.sh
  - Env vars:
    - MQTT_HOST (padrao localhost)
    - MQTT_PORT (padrao 1883)
    - API_URL (padrao http://localhost:5000)
    - TIME_WAIT (segundos para ingestao, padrao 3)
- Windows (PowerShell):
  - ./scripts/e2e-demo.ps1 -MqttHost localhost -MqttPort 1883 -ApiUrl http://localhost:5000 -SimulatorUrl http://localhost:3000
  - Parametros uteis:
    - -TimeWaitSec 3
    - -Sector A
    - -PublishDurationSec 10
    - -MaxFillAttempts 10
  - Saida: scripts/e2e-demo-output.json

Verificando o banco (Postgres)
- Conectar e consultar:
  psql -h localhost -U parking_user -d parking
  SELECT * FROM spots LIMIT 10;
  SELECT * FROM spot_events ORDER BY ts DESC LIMIT 10;
  SELECT * FROM incidents WHERE status = 'open';

Logs e troubleshooting
- Se eventos nao aparecem no backend:
  - Verifique o backend conectando ao broker (MQTT_HOST/MQTT_PORT).
  - Verifique subscription do backend: campus/parking/#.
  - Confira ConnectionStrings__Default e o status do Postgres.

Configuracao recomendada para demo
- Usar SIM_TIME_RATIO alto (ex.: 60) para demonstrar rotatividade e incidentes rapidamente.
- Ajustar STUCK_SECONDS (ex.: 10) para incidentes rapidos na demo.
