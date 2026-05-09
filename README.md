# Navarro Estacionamento - Estacionamento Inteligente (Campus)

MVP de estacionamento inteligente para um campus com 3 setores (A, B, C), cada um com 30 vagas. O sistema usa MQTT para eventos em tempo real, HTTP (REST) para consultas e relatórios, e Postgres para persistencia historica.

Resumo rapido
- Servicos: Mosquitto (MQTT), Backend (.NET HTTP + ingest), Bridge (MQTT -> HTTP), Simulador (Node.js), Postgres.
- Topicos MQTT principais:
  - Eventos de vaga: campus/parking/sectors/<sectorId>/spots/<spotId>/events
  - Gateway status: campus/parking/sectors/<sectorId>/gateway/status
  - (Opcional) recomendacoes: campus/parking/recommendations

Pre-requisitos
- Docker & docker-compose (recomendado)
- mosquitto-clients (mosquitto_pub / mosquitto_sub) ou cliente MQTT equivalente
- curl / jq (para testar HTTP)

Quickstart (Docker)
1. Na raiz do repositorio:

   docker-compose up --build

2. Servicos esperados:
- mosquitto: porta 1883
- backend: porta 5000 (HTTP)
- simulator: porta 3000 (HTTP de controle)
- bridge: conecta ao broker e encaminha eventos para o backend
- postgres: porta 5432

Rodando local (sem Docker)
- Postgres (docker rapido):
  docker run --name parking-db -e POSTGRES_DB=parking -e POSTGRES_USER=parking_user -e POSTGRES_PASSWORD=parking_pass -p 5432:5432 -d postgres:15

- Backend (.NET):
  cd backend/Backend
  dotnet restore
  setx ConnectionStrings__Default "Host=localhost;Database=parking;Username=parking_user;Password=parking_pass"
  dotnet run

- Bridge:
  cd bridge
  npm install
  setx MQTT_HOST localhost
  setx MQTT_PORT 1883
  setx BACKEND_URL http://localhost:5000
  npm start

- Simulador:
  cd simulator
  npm install
  setx MQTT_HOST localhost
  setx MQTT_PORT 1883
  setx SIM_TIME_RATIO 60
  npm start

Simulador
- Simula 90 sensores (A-01..C-30) e 3 gateways (A,B,C).
- SIM_TIME_RATIO: segundos simulados por segundo real (ex.: 60 = 1s real = 1min simulado).
- Modos de falha injetaveis (via HTTP do simulador):
  - stuck_occupied (travado como OCUPADO)
  - stuck_free (travado como LIVRE)
  - flapping (troca muito rapida)
- Endpoints do simulador (exemplos):
  - POST http://localhost:3000/simulator/failures { "spotId":"A-07", "mode":"stuck_occupied" }
  - POST http://localhost:3000/simulator/reset { "spotId":"A-07" }
  - (compat) POST http://localhost:3000/faults { "spotId":"A-07", "mode":"stuck_occupied" }

MQTT - formato de evento de vaga
Topico:
  campus/parking/sectors/A/spots/A-07/events
Payload (JSON minimo):
{
  "eventId":"<uuid>",
  "ts":"2026-04-29T10:15:30.000Z",
  "sectorId":"A",
  "spotId":"A-07",
  "state":"OCCUPIED",
  "source":"sensor"
}

Regras de ingestao do backend
- Idempotencia por eventId (nao duplicar eventos no DB).
- Atualizar estado atual em spots e registrar evento em spot_events.
- Incidentes: detectar STUCK_OCCUPIED, STUCK_FREE e FLAPPING.

HTTP API (endpoints minimos)
- GET /api/v1/map
  Retorna setores, vagas e estado atual (mapa completo).

- GET /api/v1/sectors
  Retorna occupiedCount, freeCount, occupancyRate, lastUpdateTs por setor.

- GET /api/v1/sectors/:sectorId/spots
  Lista vagas de um setor com estado e timestamps.

- GET /api/v1/sectors/:sectorId/free-spots?limit=10
  Lista vagas livres (limit opcional).

- GET /api/v1/reports/turnover?sectorId=A&from=<ISO>&to=<ISO>
  Taxa de rotatividade: numero de transicoes FREE->OCCUPIED no periodo.

- GET /api/v1/incidents?status=open
  Lista incidentes (sensor travado, flapping, etc.).

- GET /api/v1/recommendation?fromSector=A
  Retorna/genera recomendacao quando occupancyRate(fromSector) >= 0.90 e grava em recommendations_log.

Exemplos de uso (curl)
- Ver setores:
  curl -s http://localhost:5000/api/v1/sectors | jq

- Ver mapa:
  curl -s http://localhost:5000/api/v1/map | jq

- Solicitar recomendacao:
  curl -s "http://localhost:5000/api/v1/recommendation?fromSector=A" | jq

Exemplo MQTT (publicar evento manual)
mosquitto_pub -h localhost -p 1883 -t "campus/parking/sectors/A/spots/A-07/events" -m '{"eventId":"uuid-1","ts":"2026-04-29T10:15:30.000Z","sectorId":"A","spotId":"A-07","state":"OCCUPIED","source":"sensor"}' -q 1

Checklist de demonstracao
1. Subir Mosquitto + Postgres + backend + bridge + simulator (docker-compose ou local).
2. Rodar mosquitto_sub -t "campus/parking/#" -v para observar eventos.
3. Ver /api/v1/map e /api/v1/sectors atualizando em tempo real.
4. Injetar falha:
   POST /simulator/failures { "spotId":"A-07", "mode":"stuck_occupied" }
   Verificar que /api/v1/incidents?status=open contem um incidente STUCK_OCCUPIED.
5. Forcar lotacao de um setor ate occupancyRate >= 0.90 (simulador ou eventos manuais).
   GET /api/v1/recommendation?fromSector=A deve retornar recommendedSector e gravar em recommendations_log.
6. Testar idempotencia: republicar o mesmo eventId e verificar que spot_events nao duplica.

Script de demo automatizada
- scripts/e2e-demo.sh publica eventos MQTT e consulta a API.
- Padrao usa API http://localhost:5000. Ajuste via API_URL se necessario.

Verificando o banco (Postgres)
- Conectar e consultar:
  psql -h localhost -U parking_user -d parking
  SELECT * FROM spots LIMIT 10;
  SELECT * FROM spot_events ORDER BY ts DESC LIMIT 10;
  SELECT * FROM incidents WHERE status = 'open';

Logs e troubleshooting
- Se eventos nao aparecem no backend:
  - Verifique o bridge conectando ao broker e ao backend.
  - Verifique subscription do bridge: campus/parking/#.
  - Confira ConnectionStrings__Default e o status do Postgres.

Configuracao recomendada para demo
- Usar SIM_TIME_RATIO alto (ex.: 60) para demonstrar rotatividade e incidentes rapidamente.
- Ajustar STUCK_SECONDS (ex.: 10) para incidentes rapidos na demo.
