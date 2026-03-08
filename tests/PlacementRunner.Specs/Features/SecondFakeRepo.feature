Feature: Second fake repo collection runner execution

  # Same internal runner, but pointing at a different fake external repo.
  Scenario: Execute second fake Postman collection in mock mode
    Given fake repo folder "fake-postman-repo-two"
    And collection file "FakeStatusCheck.postman_collection.json"
    And environment file "environment.json"
    And authorization file "collection_authorizationservice.json"
    And mock mode is enabled
    When I run the collection runner
    Then the request name should be "Fake Status Check"
    And the resolved URL should be "http://demo-host:7001/status"
    And the status code should be 200
    And the response body should be "mock success"
