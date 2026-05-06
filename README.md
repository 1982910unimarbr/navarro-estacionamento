# Navarro Estacionamento - Estacionamento Inteligente (Campus)

Este repositório contém um MVP de um sistema de estacionamento inteligente para um campus com 3 setores (A, B, C), cada um com 30 vagas. O sistema usa MQTT para eventos em tempo real, HTTP (REST) para consultas e relatórios, e um banco de dados para histórico.

Resumo rápido
- Serviços: Mosquitto (MQTT), Backend (HTTP + MQTT ingest), Simulador (Node.js), SQLite (padrão).
- Tópicos MQTT principais:
  - Eventos de vaga: campus/parking/sectors/<sectorId>/spots/<spotId>/events
  - Gateway status: campus/parking/sectors/<sectorId>/gateway/status
  - (Opcional) recomendações: campus/parking/recommendations

Pré-requisitos
- Docker & docker-compose (recomendado) OU Node.js (v16+) + npm
- mosquitto-clients (mosquitto_pub / mosquitto_sub) ou cliente MQTT equivalente
- curl / jq (para testar HTTP)

Quickstart (Docker)
1. Na raiz do repositório:

   docker-compose up --build

2. Serviços esperados:
- mosquitto: porta 1883
- backend: porta 3000
- simulator: conecta ao broker e publica eventos
- arquivo de DB SQLite em ./data/parking.db (configurável)

Rodando local (sem Docker)
- Backend:
  cd backend
  npm install
  npm start

- Simulador:
  cd simulator
  npm install
  node simulator.js --mqtt mqtt://localhost:1883 --time-scale 1m=1s

- Mosquitto:
  usar instalação local ou docker run -it -p 1883:1883 eclipse-mosquitto

Simulador
- Simula 90 sensores (A-01..C-30) e 3 gateways (A,B,C).
- Time-scale: acelera o tempo de permanência (ex.: 1s = 1min lógico).
- Modos de falha injetáveis (via HTTP do simulador ou flags):
  - stuck_occupied (travado como OCUPADO)
  - stuck_free (travado como LIVRE)
  - flapping (troca muito rápida)
- Endpoints úteis do simulador (exemplos):
  - POST /simulator/failures { "spotId":"A-07", "mode":"stuck_occupied" }
  - POST /simulator/reset { "spotId":"A-07" }

MQTT - formato de evento de vaga
Tópico:
  campus/parking/sectors/A/spots/A-07/events
Payload (JSON mínimo):
{
  "eventId":"<uuid>",
  "ts":"2026-04-29T10:15:30.000Z",
  "sectorId":"A",
  "spotId":"A-07",
  "state":"OCCUPIED",
  "source":"sensor"
}

Regras de ingestão do backend
- Idempotência por eventId (não duplicar eventos no DB).
- Atualizar estado atual em spots e registrar evento em spot_events.
- Sugere-se QoS=1 e subscription wildcard: campus/parking/#

HTTP API (endpoints mínimos)
- GET /api/v1/map
  Retorna setores, vagas e estado atual (mapa completo).

- GET /api/v1/sectors
  Retorna occupiedCount, freeCount, occupancyRate, lastUpdateTs por setor.

- GET /api/v1/sectors/:sectorId/spots
  Lista vagas de um setor com estado e timestamps.

- GET /api/v1/sectors/:sectorId/free-spots?limit=10
  Lista vagas livres (limit opcional).

- GET /api/v1/reports/turnover?sectorId=A&from=<ISO>&to=<ISO>
  Taxa de rotatividade: número de transições FREE→OCCUPIED no período.

- GET /api/v1/incidents?status=open
  Lista incidentes (sensor travado, flapping, etc.).

- GET /api/v1/recommendation?fromSector=A
  Retorna/genera recomendação quando occupancyRate(fromSector) >= 0.90 e grava em recommendations_log.

Exemplos de uso (curl)
- Ver setores:
  curl -s http://localhost:3000/api/v1/sectors | jq

- Ver mapa:
  curl -s http://localhost:3000/api/v1/map | jq

- Solicitar recomendação:
  curl -s "http://localhost:3000/api/v1/recommendation?fromSector=A" | jq

Exemplo MQTT (publicar evento manual)
mosquitto_pub -h localhost -p 1883 -t "campus/parking/sectors/A/spots/A-07/events" -m '{"eventId":"uuid-1","ts":"2026-04-29T10:15:30.000Z","sectorId":"A","spotId":"A-07","state":"OCCUPIED","source":"sensor"}' -q 1

Testes e checklist de demonstração
1. Subir Mosquitto + backend + simulator (docker-compose ou local).
2. Rodar mosquitto_sub -t "campus/parking/#" -v para observar eventos.
3. Ver /api/v1/map e /api/v1/sectors atualizando em tempo real.
4. Injetar falha:
   POST /simulator/failures { "spotId":"A-07", "mode":"stuck_occupied" }
   Verificar que /api/v1/incidents?status=open contém um incidente STUCK_OCCUPIED.
5. Forçar lotação de um setor (simulador ou publicando eventos) até occupancyRate >= 0.90.
   GET /api/v1/recommendation?fromSector=A deve retornar um JSON com recommendedSector e gravar em recommendations_log.
6. Testar idempotência: republicar o mesmo eventId e verificar que spot_events não duplica.

Verificando o banco (SQLite)
- Arquivo padrão: ./data/parking.db
- Abrir com sqlite3:
  sqlite3 data/parking.db
  SELECT * FROM spots LIMIT 10;
  SELECT * FROM spot_events ORDER BY ts DESC LIMIT 10;
  SELECT * FROM incidents WHERE status='open';

Logs e troubleshooting
- Backend deve logar ingestão MQTT, dedupe por eventId e gravação no DB.
- Se eventos não aparecem no backend:
  - Verificar conexão do simulador com o broker.
  - Verificar subscription do backend: campus/parking/#.
  - Checar configurações de host/port no .env ou config.json.

Configuração recomendada para demo
- Usar time-scale acelerado (ex.: 1s = 1min lógico) para demonstrar rotatividade e incidentes rapidamente.
- Simulador em modo com endpoints HTTP para injetar/resetar falhas.
- Docker Compose com volumes para persistir ./data.

Contribuição e desenvolvimento
- Estrutura do repositório (exemplo):
  - backend/  (Express/Node backend, MQTT client, SQLite)
  - simulator/ (Node.js generator de eventos MQTT)
  - docker-compose.yml
  - data/ (sqlite DB)

Licença
- Veja LICENSE (se aplicável).

----
Documentação adicionada: instruções de uso, testes manuais, tópicos MQTT, endpoints HTTP e checklist da demo.
