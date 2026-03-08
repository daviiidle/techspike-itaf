Feature: Second fake repo collection runner execution

  # Same internal runner, but this repo points at the live public demo endpoint.
  Scenario: Execute second fake Postman collection as a real plumbing call
    Given fake repo folder "public-api-postman-repo"
    And collection file "FakeStatusCheck.postman_collection.json"
    And environment file "environment.json"
    And authorization file "collection_authorizationservice.json"
    And mock mode is disabled
    When I run the collection runner
    Then the request name should be "Fake Status Check"
    And the resolved URL should be "https://jsonplaceholder.typicode.com/posts/1"
    And the status code should be 200
    And the JSON response body should contain id 1
