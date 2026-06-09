.PHONY: help quickstart quickstart-down quickstart-logs build test docker-build smoke-test

TAG ?= latest

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

docker-build:  ## Build the Docker image locally
	docker build -t rfc-rag:$(TAG) .

smoke-test:  ## Run release smoke test against TAG
	TAG=$(TAG) ./scripts/smoke-test-release.sh
