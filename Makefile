.PHONY: help quickstart quickstart-down quickstart-logs build test eval eval-answers fetch-errata docker-build smoke-test pull tool-install tool-update

TAG ?= latest
ERRATA_JSON_PATH ?= errata.json

help:  ## Show this help message
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

quickstart:  ## Start the RFC RAG stack via Docker Compose
	TAG=$(TAG) docker compose --env-file .env.rfc-rag -f deploy/compose/rfc-rag.yaml up

quickstart-down:  ## Stop the RFC RAG stack and remove volumes
	docker compose -f deploy/compose/rfc-rag.yaml down -v

quickstart-logs:  ## Tail logs from the RFC RAG stack
	cd deploy/compose && docker compose -f rfc-rag.yaml logs -f

build:  ## Build the solution in Release configuration
	dotnet build RfcRag.slnx --configuration Release

test:  ## Run all tests
	./scripts/run-all-tests.sh

eval:  ## Run retrieval eval against the indexed RFC mirror (requires indexed DB and RfcMirrorPath)
	dotnet run --project src/RfcRag/ -- --eval docs/eval/golden_questions.json --corpus all

eval-answers:  ## Run real-model answer evaluation over golden questions
	dotnet run --project src/RfcRag/ -- --eval docs/eval/golden_questions.json --answers --corpus all

fetch-errata: ## Download RFC Editor errata snapshot
	curl -L https://www.rfc-editor.org/errata.json -o $(ERRATA_JSON_PATH)

docker-build:  ## Build the Docker image locally
	docker build -t rfc-rag:$(TAG) .

smoke-test:  ## Run release smoke test against TAG
	TAG=$(TAG) ./scripts/smoke-test-release.sh

pull:  ## Pull pre-built GHCR images (skips local source build)
	TAG=$(TAG) docker compose --env-file .env.rfc-rag -f deploy/compose/rfc-rag.yaml pull

tool-install:  ## Install rfc-rag as a dotnet global tool from NuGet
	dotnet tool install -g rfc-rag

tool-update:  ## Update the installed rfc-rag dotnet global tool
	dotnet tool update -g rfc-rag
