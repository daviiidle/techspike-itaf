Feature: Collection runner execution

  # High-level spike flow: point to files, run in mock mode, verify output fields.
  Scenario: Execute fake Postman collection in mock mode
    Given collection file "FakeHealthCheck.postman_collection.json"
    And environment file "environment.json"
    And authorization file "collection_authorizationservice.json"
    And mock mode is enabled
    When I run the collection runner
    Then the request name should be "Fake Health Check"
    And the resolved URL should be "http://localhost:5000/health"
    And the status code should be 200
    And the response body should be "mock success"
