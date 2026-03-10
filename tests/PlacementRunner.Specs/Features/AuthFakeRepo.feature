@postman-runner @live @auth
Feature: Postman runner live auth execution

  Scenario: Execute the auth sample repository
    Given the Postman runner targets repository "auth-postman-repo"
    And collection "AuthTestCollection.postman_collection.json"
    And mock execution is disabled
    When I execute the repository with the Postman runner
    Then the collection result should contain request "AuthTestRequest"
    And the resolved URL should be "https://httpbin.org/bearer"
    And the authorization header should be "Bearer spike-test-token"
    And the status code should be 200
    And the JSON response should contain boolean property "authenticated" with value true
