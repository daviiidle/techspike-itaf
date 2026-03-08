Feature: JsonPlaceholder demo API

  # Demo API values come from a separate environment-style file, not the feature itself.
  Scenario: Get a demo post from JsonPlaceholder
    Given the demo API environment file is "demo_api_environment.json"
    And the demo API base URL variable is "DemoApiBaseUrl"
    And the demo API resource variable is "DemoPostResource"
    And the demo API secrets file is "demo_api_secrets.json"
    And the demo API token variable is "DemoApiToken"
    When I send the demo GET request with RestSharp
    Then the demo response status should be 200
    And the demo response should contain post id 1
    And the demo response title should not be empty
