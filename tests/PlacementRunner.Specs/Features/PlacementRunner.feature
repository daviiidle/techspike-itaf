@postman-runner @mock
Feature: Postman runner mock execution

  Scenario: Execute the sample mock repository
    Given the Postman runner targets repository "mock-postman-repo"
    And collection "FakeHealthCheck.postman_collection.json"
    And mock execution is enabled
    When I execute the repository with the Postman runner
    Then the collection result should contain request "Fake Health Check"
    And the resolved URL should be "http://localhost:5000/health"
    And the status code should be 200
    And the response body should be "mock success"
