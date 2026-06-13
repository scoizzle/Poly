using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.Json;

using Poly.Introspection;

namespace Poly.Benchmarks;

// record Entity(string Name, string Description);
// record Type(string Name, string Description, Property[] Properties, Constraint[]? Rules = default, Command[]? Commands = default) : Entity(Name, Description);
// record Property(string Name, string Description, PropertyType Type, Constraint[] Constraints) : Entity(Name, Description);
// record Constraint(string Name, string Description, ConstraintType Type) : Entity(Name, Description);
// record Domain(string Name, string Description, Type[] Types, Relationship[] Relationships) : Entity(Name, Description);
// record Relationship(string Name, string Description, string SourceType, string TargetType, Cardinality Cardinality, string? ThroughType = null);

// enum Cardinality { OneToOne, OneToMany, ManyToMany }
// record Command(string Name, string Description, Parameter[] Parameters, Constraint[]? Preconditions = default, Effect[]? Effects = default, Constraint[]? Postconditions = default) : Entity(Name, Description);
// record Parameter(string Name, string Description, PropertyType Type) : Entity(Name, Description);

// abstract record PropertyType;
// record TypeReference(string TypeName) : PropertyType;
// record PrimitiveType(TypeCategory TypeCategory) : PropertyType;

// abstract record ConstraintType;
// record EqualityConstraint : ConstraintType;
// record InequalityConstraint : ConstraintType;

// abstract record ValueSource;
// record PropertyReference(string PropertyName) : ValueSource;
// record ParameterReference(string ParameterName) : ValueSource;
// record ConstantValue(object Value) : ValueSource;

// abstract record Effect;

// static class Playground {
//     public static void Main()
//     {
//         var domain = new Domain(
//             "Example Domain",
//             "An example domain for testing",
//             [
//                 new Type(
//                     Name: "Person",
//                     Description: "A person entity",
//                     Properties: [
//                         new Property("Id", "The person's unique identifier", new PrimitiveType(TypeCategory.Numeric | TypeCategory.Unsigned), [ Required(), Unique() ]),
//                         new Property("Name", "The person's name", new PrimitiveType(TypeCategory.Text), [ Required(), MinLength(1), MaxLength(200) ]),
//                         new Property("Age", "The person's age", new PrimitiveType(TypeCategory.Numeric), [ Required(), Range(0, 150) ])
//                     ],
//                     Commands: [
//                         new Command(
//                             Name: "CreatePerson",
//                             Description: "Command to create a new person",
//                             Parameters: [
//                                 new Parameter("Name", "The person's name", new PrimitiveType(TypeCategory.Text)),
//                                 new Parameter("Age", "The person's age", new PrimitiveType(TypeCategory.Numeric))
//                             ],
//                             Effects: [
//                             // Define effects here (e.g., create a new Person entity with the given name and age    
//                             ]
//                         )
//                     ]
//                 ),
//                 new Type(
//                     Name: "Company",
//                     Description: "A company entity",
//                     Properties: [
//                         new Property("Name", "The company's name", new PrimitiveType(TypeCategory.Text), [ Required(), MinLength(1), MaxLength(200) ]),
//                     ]
//                 ),
//                 new Type(
//                     Name: "EmploymentRecord",
//                     Description: "An employment record entity",
//                     Properties: [
//                         new Property("StartDate", "The start date of the employment", new PrimitiveType(TypeCategory.Temporal), [ Required() ]),
//                         new Property("EndDate", "The end date of the employment", new PrimitiveType(TypeCategory.Temporal), [ Optional() ])
//                     ]
//                 ),
//                 new Type(
//                     Name: "Project",
//                     Description: "A project entity",
//                     Properties: [
//                         new Property("Name", "The project's name", new PrimitiveType(TypeCategory.Text), [ Required(), MinLength(1), MaxLength(200) ])
//                     ]
//                 ),
//                 new Type(
//                     Name: "Assignment",
//                     Description: "An assignment entity",
//                     Properties: [
//                         new Property("Role", "The person's role in the project", new PrimitiveType(TypeCategory.Text), [ Required(), MinLength(1), MaxLength(200) ]),
//                         new Property("StartDate", "The start date of the assignment", new PrimitiveType(TypeCategory.Temporal), [ Required() ]),
//                         new Property("EndDate", "The end date of the assignment", new PrimitiveType(TypeCategory.Temporal), [ Optional() ])
//                     ]
//                 )
//             ],
//             Relationships: [
//                 new Relationship("Employment", "A person's employment at a company", "Person", "Company", Cardinality.ManyToMany, ThroughType: "EmploymentRecord"),
//                 new Relationship("ProjectAssignment", "A person's assignment to a project", "Person", "Project", Cardinality.ManyToMany, ThroughType: "Assignment")
//             ]
//         );

//         Console.WriteLine(JsonSerializer.Serialize(domain, new JsonSerializerOptions { WriteIndented = true }));
//     }


//     static Constraint Unique() => new Constraint("Unique", "The property must be unique across all instances", new EqualityConstraint());
//     static Constraint Required() => new Constraint("Required", "The property is required", new EqualityConstraint());
//     static Constraint Optional() => new Constraint("Optional", "The property is optional", new EqualityConstraint());
//     static Constraint MinLength(int length) => new Constraint("MinLength", $"The property must have a minimum length of {length}", new EqualityConstraint());
//     static Constraint MaxLength(int length) => new Constraint("MaxLength", $"The property must have a maximum length of {length}", new EqualityConstraint());
//     static Constraint Range(double min, double max) => new Constraint("Range", $"The property must be between {min} and {max}", new EqualityConstraint());
//     static Constraint GreaterThan(ValueSource valueSource) => new Constraint("GreaterThan", $"The property must be greater than {valueSource}", new InequalityConstraint());
//     static Constraint LessThan(ValueSource valueSource) => new Constraint("LessThan", $"The property must be less than {valueSource}", new InequalityConstraint());
// }