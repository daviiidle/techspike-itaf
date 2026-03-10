@postman-runner @live @public-api
Feature: Postman runner live public API execution

  Scenario: Execute the public API sample repository
    Given the Postman runner targets repository "public-api-postman-repo"
    And collection "FakeStatusCheck.postman_collection.json"
    And mock execution is disabled
    When I execute the repository with the Postman runner
    Then the collection result should contain request "Fake Status Check"
    And the resolved URL should be "https://jsonplaceholder.typicode.com/posts/1"
    And the status code should be 200
    And the JSON response should contain integer property "id" with value 1
