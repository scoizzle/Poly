using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Benchmarks.DomainModeling.Demos;

internal static class HealthcareDomain {
    public static Domain Build() {
        var domain = new Domain("Healthcare Patient Management");

        CreatePrimitives(domain);
        CreateEntities(domain);
        CreateStages(domain);
        CreateEvents(domain);
        CreateActions(domain);
        CreateRelationships(domain);
        CreatePolicies(domain);

        return domain;
    }

    private static void CreatePrimitives(Domain domain) {
        domain.AddType(new Primitive(domain, "string", TypeCategory.Text));
        domain.AddType(new Primitive(domain, "int", TypeCategory.Integer));
        domain.AddType(new Primitive(domain, "decimal", TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "bool", TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "instant", TypeCategory.Instant));
        domain.AddType(new Primitive(domain, "date", TypeCategory.Primitive));
    }

    private static void CreateEntities(Domain domain) {
        var stringType = domain.RequirePrimitive("string");
        var intType = domain.RequirePrimitive("int");
        var decimalType = domain.RequirePrimitive("decimal");
        var boolType = domain.RequirePrimitive("bool");
        var dateType = domain.RequirePrimitive("date");

        var person = new Entity(domain, "Person");
        person.AddProperty(new Property(domain, "FirstName", stringType));
        person.AddProperty(new Property(domain, "LastName", stringType));
        person.AddProperty(new Property(domain, "DateOfBirth", dateType));
        person.AddProperty(new Property(domain, "PhoneNumber", stringType));
        person.AddProperty(new Property(domain, "Email", stringType));
        domain.AddType(person);

        var patient = new Entity(domain, "Patient", person);
        patient.AddProperty(new Property(domain, "PatientId", stringType));
        patient.AddProperty(new Property(domain, "InsuranceProvider", stringType));
        patient.AddProperty(new Property(domain, "InsuranceNumber", stringType));
        patient.AddProperty(new Property(domain, "BloodType", stringType));
        patient.AddProperty(new Property(domain, "Allergies", stringType));
        domain.AddType(patient);

        var medicalStaff = new Entity(domain, "MedicalStaff", person);
        medicalStaff.AddProperty(new Property(domain, "EmployeeId", stringType));
        medicalStaff.AddProperty(new Property(domain, "Department", stringType));
        medicalStaff.AddProperty(new Property(domain, "LicenseNumber", stringType));
        domain.AddType(medicalStaff);

        var doctor = new Entity(domain, "Doctor", medicalStaff);
        doctor.AddProperty(new Property(domain, "Specialty", stringType));
        doctor.AddProperty(new Property(domain, "IsBoardCertified", boolType));
        domain.AddType(doctor);

        var nurse = new Entity(domain, "Nurse", medicalStaff);
        nurse.AddProperty(new Property(domain, "CertificationLevel", stringType));
        nurse.AddProperty(new Property(domain, "Ward", stringType));
        domain.AddType(nurse);

        var appointment = new Entity(domain, "Appointment");
        appointment.AddProperty(new Property(domain, "AppointmentDate", domain.RequirePrimitive("instant")));
        appointment.AddProperty(new Property(domain, "Duration", intType));
        appointment.AddProperty(new Property(domain, "Reason", stringType));
        appointment.AddProperty(new Property(domain, "Notes", stringType));
        appointment.AddProperty(new Property(domain, "Status", stringType));
        domain.AddType(appointment);

        var medicalRecord = new Entity(domain, "MedicalRecord");
        medicalRecord.AddProperty(new Property(domain, "RecordDate", domain.RequirePrimitive("instant")));
        medicalRecord.AddProperty(new Property(domain, "Diagnosis", stringType));
        medicalRecord.AddProperty(new Property(domain, "Treatment", stringType));
        medicalRecord.AddProperty(new Property(domain, "Prescription", stringType));
        medicalRecord.AddProperty(new Property(domain, "Notes", stringType));
        domain.AddType(medicalRecord);

        var billing = new Entity(domain, "Billing");
        billing.AddProperty(new Property(domain, "Amount", decimalType));
        billing.AddProperty(new Property(domain, "ServiceDate", domain.RequirePrimitive("instant")));
        billing.AddProperty(new Property(domain, "IsPaid", boolType));
        billing.AddProperty(new Property(domain, "InsuranceClaimId", stringType));
        domain.AddType(billing);

        var room = new Entity(domain, "Room");
        room.AddProperty(new Property(domain, "RoomNumber", stringType));
        room.AddProperty(new Property(domain, "RoomType", stringType));
        room.AddProperty(new Property(domain, "Floor", intType));
        room.AddProperty(new Property(domain, "IsAvailable", boolType));
        domain.AddType(room);
    }

    private static void CreateStages(Domain domain) {
        var appointment = domain.RequireEntity("Appointment");
        var medicalRecord = domain.RequireEntity("MedicalRecord");

        appointment.AddStage(new Stage(domain, "Scheduled"));
        appointment.AddStage(new Stage(domain, "Scheduled"));
        appointment.AddStage(new Stage(domain, "Confirmed"));
        appointment.AddStage(new Stage(domain, "InProgress"));
        appointment.AddStage(new Stage(domain, "Completed"));
        appointment.AddStage(new Stage(domain, "Cancelled"));
        appointment.AddStage(new Stage(domain, "NoShow"));

        medicalRecord.AddStage(new Stage(domain, "Draft"));
        medicalRecord.AddStage(new Stage(domain, "Review"));
        medicalRecord.AddStage(new Stage(domain, "Finalized"));
        medicalRecord.AddStage(new Stage(domain, "Amended"));
    }

    private static void CreateEvents(Domain domain) {
        var appointment = domain.RequireEntity("Appointment");
        var patient = domain.RequireEntity("Patient");
        var billing = domain.RequireEntity("Billing");
        var stringType = domain.RequirePrimitive("string");
        var instantType = domain.RequirePrimitive("instant");
        var decimalType = domain.RequirePrimitive("decimal");

        var appointmentScheduled = new Event(domain, "AppointmentScheduled");
        appointmentScheduled.AddProperty(new Property(domain, "PatientName", stringType));
        appointmentScheduled.AddProperty(new Property(domain, "DoctorName", stringType));
        appointment.AddEvent(appointmentScheduled);
        domain.AddType(appointmentScheduled);

        var appointmentCompleted = new Event(domain, "AppointmentCompleted");
        appointmentCompleted.AddProperty(new Property(domain, "Diagnosis", stringType));
        appointment.AddEvent(appointmentCompleted);
        domain.AddType(appointmentCompleted);

        var medicalRecordCreated = new Event(domain, "MedicalRecordCreated");
        medicalRecordCreated.AddProperty(new Property(domain, "RecordId", stringType));
        patient.AddEvent(medicalRecordCreated);
        domain.AddType(medicalRecordCreated);

        var paymentReceived = new Event(domain, "PaymentReceived");
        paymentReceived.AddProperty(new Property(domain, "PaymentDate", instantType));
        paymentReceived.AddProperty(new Property(domain, "Amount", decimalType));
        billing.AddEvent(paymentReceived);
        domain.AddType(paymentReceived);

        var roomAssigned = new Event(domain, "RoomAssigned");
        roomAssigned.AddProperty(new Property(domain, "RoomNumber", stringType));
        appointment.AddEvent(roomAssigned);
        domain.AddType(roomAssigned);
    }

    private static void CreateActions(Domain domain) {
        var appointment = domain.RequireEntity("Appointment");
        var medicalRecord = domain.RequireEntity("MedicalRecord");
        var billing = domain.RequireEntity("Billing");
        var patient = domain.RequireEntity("Patient");
        var doctor = domain.RequireEntity("Doctor");
        var room = domain.RequireEntity("Room");
        var stringType = domain.RequirePrimitive("string");
        var instantType = domain.RequirePrimitive("instant");
        var decimalType = domain.RequirePrimitive("decimal");

        var scheduleAction = new DomainAction(domain, "ScheduleAppointment", appointment);
        scheduleAction.AddEffect(new StageTransition(domain) { TargetStage = appointment.RequireStage("Scheduled") });
        var publishScheduled = new PublishEvent(domain) { Event = appointment.RequireEvent("AppointmentScheduled") };
        scheduleAction.AddEffect(publishScheduled);
        appointment.AddAction(scheduleAction);

        var confirmAction = new DomainAction(domain, "ConfirmAppointment", appointment);
        confirmAction.AddEffect(new StageTransition(domain) { TargetStage = appointment.RequireStage("Confirmed") });
        appointment.AddAction(confirmAction);
        appointment.RequireStage("Scheduled").AddAction(confirmAction);

        var startAction = new DomainAction(domain, "StartAppointment", appointment);
        startAction.AddEffect(new StageTransition(domain) { TargetStage = appointment.RequireStage("InProgress") });
        appointment.AddAction(startAction);
        appointment.RequireStage("Confirmed").AddAction(startAction);

        var completeAction = new DomainAction(domain, "CompleteAppointment", appointment);
        completeAction.AddEffect(new StageTransition(domain) { TargetStage = appointment.RequireStage("Completed") });
        var publishCompleted = new PublishEvent(domain) { Event = appointment.RequireEvent("AppointmentCompleted") };
        completeAction.AddEffect(publishCompleted);
        completeAction.AddEffect(new CreateEntityInstance(domain) {
            EntityType = medicalRecord,
            InitialStage = medicalRecord.RequireStage("Draft")
        });
        appointment.AddAction(completeAction);
        appointment.RequireStage("InProgress").AddAction(completeAction);

        var cancelAction = new DomainAction(domain, "CancelAppointment", appointment);
        cancelAction.AddEffect(new StageTransition(domain) { TargetStage = appointment.RequireStage("Cancelled") });
        appointment.AddAction(cancelAction);
        appointment.RequireStage("Scheduled").AddAction(cancelAction);
        appointment.RequireStage("Confirmed").AddAction(cancelAction);

        var noShowAction = new DomainAction(domain, "MarkNoShow", appointment);
        noShowAction.AddEffect(new StageTransition(domain) { TargetStage = appointment.RequireStage("NoShow") });
        appointment.AddAction(noShowAction);
        appointment.RequireStage("Confirmed").AddAction(noShowAction);

        var createRecordAction = new DomainAction(domain, "CreateMedicalRecord", medicalRecord);
        createRecordAction.AddEffect(new StageTransition(domain) { TargetStage = medicalRecord.RequireStage("Draft") });
        medicalRecord.AddAction(createRecordAction);

        var finalizeRecordAction = new DomainAction(domain, "FinalizeRecord", medicalRecord);
        finalizeRecordAction.AddEffect(new StageTransition(domain) { TargetStage = medicalRecord.RequireStage("Finalized") });
        medicalRecord.AddAction(finalizeRecordAction);
        medicalRecord.RequireStage("Draft").AddAction(finalizeRecordAction);
        medicalRecord.RequireStage("Review").AddAction(finalizeRecordAction);

        var processPaymentAction = new DomainAction(domain, "ProcessPayment", billing);
        var paymentDateParam = new Property(domain, "PaymentDate", instantType);
        var paymentAmountParam = new Property(domain, "Amount", decimalType);
        processPaymentAction.AddParameter(paymentDateParam);
        processPaymentAction.AddParameter(paymentAmountParam);
        var publishPayment = new PublishEvent(domain) { Event = billing.RequireEvent("PaymentReceived") };
        publishPayment.BindProperty(publishPayment.Event.RequireProperty("PaymentDate"), paymentDateParam);
        publishPayment.BindProperty(publishPayment.Event.RequireProperty("Amount"), paymentAmountParam);
        processPaymentAction.AddEffect(publishPayment);
        billing.AddAction(processPaymentAction);

        var assignRoomAction = new DomainAction(domain, "AssignRoom", appointment);
        var roomNumberParam = new Property(domain, "RoomNumber", stringType);
        assignRoomAction.AddParameter(roomNumberParam);
        var publishRoom = new PublishEvent(domain) { Event = appointment.RequireEvent("RoomAssigned") };
        publishRoom.BindProperty(publishRoom.Event.RequireProperty("RoomNumber"), roomNumberParam);
        assignRoomAction.AddEffect(publishRoom);
        appointment.AddAction(assignRoomAction);
        appointment.RequireStage("Scheduled").AddAction(assignRoomAction);
        appointment.RequireStage("Confirmed").AddAction(assignRoomAction);
    }

    private static void CreateRelationships(Domain domain) {
        var patient = domain.RequireEntity("Patient");
        var doctor = domain.RequireEntity("Doctor");
        var appointment = domain.RequireEntity("Appointment");
        var medicalRecord = domain.RequireEntity("MedicalRecord");
        var billing = domain.RequireEntity("Billing");
        var room = domain.RequireEntity("Room");

        // PatientAppointments: Patient owns Appointment
        var patientAppointments = new Relationship(domain, "PatientAppointments", patient, appointment, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(patientAppointments);
        patient.AddRelationship(patientAppointments);

        // DoctorAppointments: Doctor does NOT own Appointment (to avoid multiple ownership)
        var doctorAppointments = new Relationship(domain, "DoctorAppointments", doctor, appointment, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(doctorAppointments);
        doctor.AddRelationship(doctorAppointments);

        // PatientRecords: Patient owns MedicalRecord
        var patientRecords = new Relationship(domain, "PatientRecords", patient, medicalRecord, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(patientRecords);
        patient.AddRelationship(patientRecords);

        // AppointmentRecords: Appointment does NOT own MedicalRecord (to avoid multiple ownership of MedicalRecord)
        var appointmentRecords = new Relationship(domain, "AppointmentRecords", appointment, medicalRecord, RelationshipCardinality.OneToOne, false);
        domain.AddRelationship(appointmentRecords);
        appointment.AddRelationship(appointmentRecords);

        // PatientBilling: Patient owns Billing
        var patientBilling = new Relationship(domain, "PatientBilling", patient, billing, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(patientBilling);
        patient.AddRelationship(patientBilling);

        // DoctorBilling: Doctor does NOT own Billing (to avoid multiple ownership)
        var doctorBilling = new Relationship(domain, "DoctorBilling", doctor, billing, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(doctorBilling);
        doctor.AddRelationship(doctorBilling);

        // RoomAppointments: Room does NOT own Appointment
        var roomAppointments = new Relationship(domain, "RoomAppointments", room, appointment, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(roomAppointments);
    }

    private static void CreatePolicies(Domain domain) {
        var patient = domain.RequireEntity("Patient");
        var medicalRecord = domain.RequireEntity("MedicalRecord");
        var billing = domain.RequireEntity("Billing");
        var room = domain.RequireEntity("Room");

        var requireInsurance = new Policy(domain, "RequireInsuranceForPatients") { AggregationStrategy = PolicyAggregationStrategy.All };
        requireInsurance.AddRule(new PropertyRule(domain, "InsuranceProviderRequired", patient.RequireProperty("InsuranceProvider"), new RequiredConstraint()));
        patient.AddPolicy(requireInsurance);

        var requireDiagnosis = new Policy(domain, "RequireDiagnosisForCompleted");
        requireDiagnosis.AddRule(new PropertyRule(domain, "DiagnosisRequiredWhenFinalized", medicalRecord.RequireProperty("Diagnosis"), new RequiredConstraint()));
        medicalRecord.RequireStage("Finalized").AddPolicy(requireDiagnosis);

        var requirePayment = new Policy(domain, "RequirePaymentBeforeDischarge") { AggregationStrategy = PolicyAggregationStrategy.All };
        requirePayment.AddRule(new PropertyRule(domain, "IsPaidRequired", billing.RequireProperty("IsPaid"), new RequiredConstraint()));
        billing.AddPolicy(requirePayment);

        var roomAvailable = new Policy(domain, "RoomMustBeAvailable");
        roomAvailable.AddRule(new PropertyRule(domain, "IsAvailableRequired", room.RequireProperty("IsAvailable"), new RequiredConstraint()));
        room.AddPolicy(roomAvailable);
    }
}