.PHONY: dev dev-db dev-stop test test-backend test-frontend test-e2e

# Start everything for local development (PostgreSQL + backend + frontend)
dev: dev-db
	@echo "Starting backend (dotnet watch) and frontend (astro dev)..."
	@trap 'kill 0' EXIT; \
		cd backend/src/Kalandra.Api && dotnet watch run & \
		cd frontend && npm run dev & \
		wait

# Start only the database
dev-db:
	@echo "Starting PostgreSQL..."
	@cd backend && docker compose up db -d --wait

# Stop all development services
dev-stop:
	@echo "Stopping PostgreSQL..."
	@cd backend && docker compose down

# Run all tests
test: test-backend test-frontend

# Run backend integration tests (requires Docker for Testcontainers)
test-backend:
	@echo "Running backend tests..."
	@cd backend && dotnet test --verbosity normal

# Run frontend tests (Playwright)
test-frontend:
	@echo "Running frontend tests..."
	@cd frontend && npx playwright test

# Run full e2e tests (starts backend + frontend, runs Playwright)
test-e2e: dev-db
	@echo "Building frontend..."
	@cd frontend && npm run build
	@echo "Running e2e tests..."
	@trap 'kill 0' EXIT; \
		cd backend/src/Kalandra.Api && dotnet run --urls http://localhost:5000 & \
		sleep 3 && \
		cd frontend && npx serve dist -l 4321 & \
		sleep 2 && \
		cd frontend && npx playwright test --config playwright.e2e.config.ts && \
		kill 0
