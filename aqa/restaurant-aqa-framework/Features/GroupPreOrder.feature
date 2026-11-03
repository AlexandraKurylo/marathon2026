@api @group-pre-order
Feature: Group pre-order

  The owner of a booking invites friends. Every guest who follows the invitation fills
  their own cart, the owner sees all carts together, can remove a participant and
  confirms one consolidated pre-order that is passed to the restaurant's waiter.
  Everything a scenario creates (location, dishes, tables, waiter, bookings) belongs
  to that scenario only and is removed again afterwards.

  Background:
    Given the restaurant has a waiter account
    And a location with its own waiter, two tables and 3 dishes on the menu
    And another customer is signed in

  Rule: The owner sees who joined the group and what they ordered

    Background:
      Given the owner has booked the table for a group of 6

    @level1 @smoke
    Scenario: The owner sees every participant with their dishes and totals
      Given guest "Andrii" has joined the group
      And "Andrii" has put 2 portions of dish 1 in the cart
      And guest "Olena" has joined the group
      And "Olena" has put 1 portion of dish 2 in the cart
      When the owner looks at the participants
      Then the participants are:
        | name   |
        | Andrii |
        | Olena  |
      And "Andrii" has 2 portions of dish 1
      And "Olena" has 1 portion of dish 2
      And every participant's total matches their dishes
      And the group total is the sum of the participants' totals

    @level1
    Scenario: The group total follows a guest changing their cart
      Given guest "Andrii" has joined the group
      And "Andrii" has put 1 portion of dish 1 in the cart
      And the owner has looked at the participants
      When "Andrii" puts 2 more portions of dish 3 in the cart
      And the owner looks at the participants again
      Then "Andrii" has 2 portions of dish 3
      And the group total has grown by the price of 2 portions of dish 3
      And the group total is the sum of the participants' totals

    @level1
    Scenario: Guests who have not confirmed or not chosen anything are listed too
      Given guest "Andrii" has joined the group
      And "Andrii" has put 1 portion of dish 1 in the cart
      And "Andrii" has confirmed the cart
      And guest "Olena" has joined the group
      And "Olena" has put 1 portion of dish 2 in the cart
      And guest "Taras" has joined the group
      When the owner looks at the participants
      Then "Andrii" is marked as confirmed
      And "Olena" is marked as not confirmed yet
      And "Taras" is listed without dishes
      And "Taras" is marked as not confirmed yet

    @level1
    Scenario: Nobody has joined the group yet
      When the owner looks at the participants
      Then no participants are listed
      And the group total is 0

    @level1 @negative
    Scenario Outline: Only the owner may see the participants
      Given guest "Andrii" has joined the group
      When "<who>" tries to look at the participants
      Then access to the group is refused

      Examples: People who are not the owner
        | who              |
        | another customer |
        | waiter           |
        | Andrii           |
        | visitor          |

    @level1 @negative
    Scenario: The owner looks at a group that does not exist
      When the owner looks at the participants of a group that does not exist
      Then the owner is told the group could not be found

  Rule: The owner confirms one consolidated pre-order

    Background:
      Given the owner has booked the table for a group of 6

    @level2 @smoke
    Scenario: Confirmed carts are combined into one pre-order for the restaurant
      Given guest "Andrii" has joined the group
      And "Andrii" has put 2 portions of dish 1 in the cart
      And "Andrii" has confirmed the cart
      And guest "Olena" has joined the group
      And "Olena" has put 1 portion of dish 1 in the cart
      And "Olena" has put 1 portion of dish 2 in the cart
      And "Olena" has confirmed the cart
      And guest "Taras" has joined the group
      And "Taras" has put 3 portions of dish 3 in the cart
      When the owner confirms the group pre-order
      Then the pre-order is passed to the restaurant with:
        | dish | portions |
        | 1    | 3        |
        | 2    | 1        |
      And dish 3 is not in the pre-order
      And new guests can no longer join the group

    @level2
    Scenario: The waiter sees the pre-order the owner confirmed
      Given guest "Andrii" has joined the group
      And "Andrii" has put 2 portions of dish 2 in the cart
      And "Andrii" has confirmed the cart
      And guest "Olena" has joined the group
      And "Olena" has put 1 portion of dish 2 in the cart
      And "Olena" has confirmed the cart
      When the owner confirms the group pre-order
      Then the waiter sees the confirmed pre-order with:
        | dish | portions |
        | 2    | 3        |

    @level2
    Scenario: The owner has one own place in the group
      When the owner asks for their own place in the group
      And the owner asks for their own place in the group again
      Then the owner gets the same place both times
      When the owner looks at the participants
      Then the owner is listed as the owner of the group
      And no guests other than the owner are listed

    @level2 @negative
    Scenario: Nothing can be confirmed while no guest has confirmed a cart
      Given guest "Andrii" has joined the group
      And "Andrii" has put 1 portion of dish 1 in the cart
      When the owner confirms the group pre-order
      Then the owner is told "There are no submitted guest sessions in this group."

    # A guest cannot confirm an empty cart and cannot change a cart once it is confirmed,
    # so a confirmed cart without dishes cannot be produced through the service.
    @level2 @negative @wip
    Scenario: Nothing can be confirmed when the confirmed carts hold no dishes
      Given guest "Andrii" has joined the group
      And "Andrii" has an empty confirmed cart
      When the owner confirms the group pre-order
      Then the owner is told "Submitted guest sessions do not contain any dishes."

    @level2 @negative
    Scenario: The owner confirms the pre-order of a group that does not exist
      When the owner confirms the pre-order of a group that does not exist
      Then the owner is told the group could not be found

    @level2 @negative
    Scenario: A group pre-order cannot be confirmed twice
      Given guest "Andrii" has joined the group
      And "Andrii" has put 1 portion of dish 1 in the cart
      And "Andrii" has confirmed the cart
      And the owner has confirmed the group pre-order
      When the owner confirms the group pre-order again
      Then the owner is told "Group reservation was submitted."

    @level2 @negative
    Scenario Outline: Only the owner may confirm the group pre-order
      Given guest "Andrii" has joined the group
      And "Andrii" has put 1 portion of dish 1 in the cart
      And "Andrii" has confirmed the cart
      When "<who>" tries to confirm the group pre-order
      Then access to the group is refused

      Examples: People who are not the owner
        | who              |
        | another customer |
        | waiter           |
        | visitor          |

  Rule: A group pre-order needs a started pre-order to be confirmed

    Background:
      Given the owner has booked the table for a group of 6 without starting a pre-order

    @level2 @negative
    Scenario: Nothing can be confirmed when the booking has no pre-order
      Given guest "Andrii" has joined the group
      And "Andrii" has put 1 portion of dish 1 in the cart
      And "Andrii" has confirmed the cart
      When the owner confirms the group pre-order
      Then the owner is told "was not found"

  Rule: The owner can remove a participant from the group

    Background:
      Given the owner has booked the table for a group of 6
      And guest "Andrii" has joined the group
      And "Andrii" has put 2 portions of dish 1 in the cart

    @level3 @smoke
    Scenario: The owner removes one of several participants
      Given guest "Olena" has joined the group
      And "Olena" has put 1 portion of dish 2 in the cart
      And guest "Taras" has joined the group
      And "Taras" has put 1 portion of dish 3 in the cart
      When the owner removes "Olena" from the group
      Then the participants are:
        | name   |
        | Andrii |
        | Taras  |
      And "Olena" is not listed
      And the total of the remaining participants is the group total
      And every participant's total matches their dishes

    @level3
    Scenario: The owner removes a participant who has already confirmed their cart
      Given "Andrii" has confirmed the cart
      And guest "Olena" has joined the group
      When the owner removes "Andrii" from the group
      Then the participants are:
        | name  |
        | Olena |

    @level3
    Scenario: The owner removes the last participant
      When the owner removes "Andrii" from the group
      Then no participants are listed
      And the group total is 0

    @level3 @negative
    Scenario: A participant of another group cannot be removed from this one
      Given the owner has also booked the second table for a group of 4
      And guest "Petro" has joined the second group
      When the owner removes "Petro" from the group
      Then the owner is told "Guest session does not belong to this group reservation."

    @level3 @negative
    Scenario: A participant who does not exist cannot be removed
      When the owner removes a participant who does not exist
      Then the owner is told the participant could not be found

    @level3 @negative
    Scenario Outline: Only the owner may remove a participant
      Given guest "Olena" has joined the group
      When "<who>" tries to remove "Olena" from the group
      Then access to the group is refused

      Examples: People who are not the owner
        | who              |
        | another customer |
        | waiter           |
        | Andrii           |
        | visitor          |

    @level3 @e2e
    Scenario: A removed participant's dishes do not reach the restaurant
      Given "Andrii" has confirmed the cart
      And guest "Olena" has joined the group
      And "Olena" has put 1 portion of dish 2 in the cart
      And "Olena" has confirmed the cart
      And guest "Taras" has joined the group
      And "Taras" has put 4 portions of dish 3 in the cart
      And "Taras" has confirmed the cart
      And the owner has looked at the participants
      When the owner removes "Taras" from the group
      And the owner confirms the group pre-order
      Then the pre-order is passed to the restaurant with:
        | dish | portions |
        | 1    | 2        |
        | 2    | 1        |
      And the waiter sees the confirmed pre-order with:
        | dish | portions |
        | 1    | 2        |
        | 2    | 1        |
