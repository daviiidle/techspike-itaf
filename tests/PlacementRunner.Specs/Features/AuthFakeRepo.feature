Feature: Auth fake repo collection runner execution

  # Uses a live public auth endpoint and validates the bearer header plumbing.
  Scenario: Execute auth-enabled Postman collection as a real plumbing call
    Given fake repo folder "auth-postman-repo"
    And collection file "AuthTestCollection.postman_collection.json"
    And environment file "environment.json"
    And authorization file "collection_authorizationservice.json"
    And mock mode is disabled
    When I run the collection runner
    Then the request name should be "AuthTestRequest"
    And the resolved URL should be "https://httpbin.org/bearer"
    And the authorization header should be "Bearer spike-test-token"
    And the status code should be 200
    And the JSON response body should contain authenticated true
