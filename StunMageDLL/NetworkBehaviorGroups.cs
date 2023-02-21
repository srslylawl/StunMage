

namespace STUN {
    public enum IncomingBehaviorGroup {
        A_Open = 0,
        B_RequiresMapping = 1,
        C_RequiresSendToIP = 2,
        D_RequiresSendToIPAndPort = 3,
        E_Blocked = 4
    }

    public enum OutgoingBehaviorGroup {
        Predictable = 0,
        Queryable = 1,
        Unpredictable = 2
    }

    public enum ConnectionTechnique {
        None = 0,
        HolePunch = 100,
        Brute_Force_Or_Luck = 99999,
    }

    public enum ConnectionCondition {
        None = 0,
        ReceiverHasToAllocate = 1,
        ReceiverHasToQuery = 2,
        SenderHasToQuery = 10,
        BothHaveToQuery = 20,
        Incompatible = 99999
    }

    public enum BestRoleForClient {
        Either,
        Sender,
        Receiver
    }

    public struct ConnectionPlan {
        public ConnectionTechnique ConnectionTechnique;
        public ConnectionCondition ConnectionCondition;
    }

    public struct ConnectionEvaluation {
        public bool ConnectionPossibleWithoutBruteForce;
        public ConnectionPlan BestEvaluation;
        public BestRoleForClient BestRoleForClient;
    }

    public struct EndPointBehaviorTuple {
        public IncomingBehaviorGroup IncomingBehaviorGroup;
        public OutgoingBehaviorGroup OutgoingBehaviorGroup;

        private ConnectionPlan Evaluate(EndPointBehaviorTuple other) {
            if (IncomingBehaviorGroup == IncomingBehaviorGroup.E_Blocked || other.IncomingBehaviorGroup == IncomingBehaviorGroup.E_Blocked) {
                return new ConnectionPlan() {
                    ConnectionTechnique = ConnectionTechnique.Brute_Force_Or_Luck,
                    ConnectionCondition = ConnectionCondition.Incompatible
                };
            }

            switch (other.IncomingBehaviorGroup) {
                //other is open
                case IncomingBehaviorGroup.A_Open:
                    return new ConnectionPlan() {
                        ConnectionTechnique = ConnectionTechnique.None,
                        ConnectionCondition = ConnectionCondition.None
                    };
                //other is full cone
                case IncomingBehaviorGroup.B_RequiresMapping: {
                    ConnectionPlan evaluation = new ConnectionPlan() {
                        ConnectionTechnique = ConnectionTechnique.None
                    };

                    evaluation.ConnectionCondition =
                        other.OutgoingBehaviorGroup == OutgoingBehaviorGroup.Predictable
                        ? ConnectionCondition.ReceiverHasToAllocate
                        : ConnectionCondition.ReceiverHasToQuery;

                    return evaluation;
                }
                //other is ip restricted
                case IncomingBehaviorGroup.C_RequiresSendToIP: {
                    if (other.OutgoingBehaviorGroup == OutgoingBehaviorGroup.Unpredictable) {
                        return new ConnectionPlan() {
                            ConnectionCondition = ConnectionCondition.None,
                            ConnectionTechnique = ConnectionTechnique.Brute_Force_Or_Luck
                        };
                    }

                    ConnectionPlan evaluation = new ConnectionPlan() {
                        ConnectionTechnique = ConnectionTechnique.HolePunch
                    };

                    if (other.OutgoingBehaviorGroup == OutgoingBehaviorGroup.Predictable) {
                        evaluation.ConnectionCondition = other.OutgoingBehaviorGroup == OutgoingBehaviorGroup.Predictable
                            ? ConnectionCondition.None
                            : ConnectionCondition.ReceiverHasToQuery;
                    }
                    return evaluation;
                }
                //other is ip + port restricted
                case IncomingBehaviorGroup.D_RequiresSendToIPAndPort:
                    if (other.OutgoingBehaviorGroup == OutgoingBehaviorGroup.Unpredictable || OutgoingBehaviorGroup == OutgoingBehaviorGroup.Unpredictable) {
                        return new ConnectionPlan() {
                            ConnectionCondition = ConnectionCondition.None,
                            ConnectionTechnique = ConnectionTechnique.Brute_Force_Or_Luck
                        };
                    }

                    var eval = new ConnectionPlan() {
                        ConnectionTechnique = ConnectionTechnique.HolePunch
                    };

                    if (other.OutgoingBehaviorGroup == OutgoingBehaviorGroup.Predictable) {
                        eval.ConnectionCondition = OutgoingBehaviorGroup == OutgoingBehaviorGroup.Predictable 
                            ? ConnectionCondition.None 
                            : ConnectionCondition.SenderHasToQuery;
                        return eval;
                    }

                    eval.ConnectionCondition = OutgoingBehaviorGroup == OutgoingBehaviorGroup.Predictable
                            ? ConnectionCondition.ReceiverHasToQuery : ConnectionCondition.BothHaveToQuery;
                    return eval;
            }

            throw new System.Exception($"Unexpected path - this: {IncomingBehaviorGroup}, {OutgoingBehaviorGroup} | other: {other.IncomingBehaviorGroup}, {other.OutgoingBehaviorGroup}");
        }

        public ConnectionEvaluation GetBestResult(EndPointBehaviorTuple other) {
            var evaluation_As_Sender = Evaluate(other);
            var evaluation_As_Receiver = other.Evaluate(this);
            int senderCost = (int)evaluation_As_Sender.ConnectionTechnique + (int)evaluation_As_Sender.ConnectionCondition;
            int receiverCost = (int)evaluation_As_Receiver.ConnectionTechnique + (int)evaluation_As_Receiver.ConnectionCondition;


            bool senderIsBetter = senderCost < receiverCost;
            var bestPlan = senderIsBetter ? evaluation_As_Sender : evaluation_As_Receiver;
            if (bestPlan.ConnectionTechnique == ConnectionTechnique.Brute_Force_Or_Luck || bestPlan.ConnectionCondition == ConnectionCondition.Incompatible) {
                return new ConnectionEvaluation() {
                    ConnectionPossibleWithoutBruteForce = false,
                    BestRoleForClient = BestRoleForClient.Either,
                    BestEvaluation = bestPlan,
                };
            }
            BestRoleForClient bestRole = BestRoleForClient.Either;
            if (senderIsBetter) bestRole = BestRoleForClient.Sender;
            //cant use !senderIsBetter as then it would also overwrite when equal
            if (receiverCost < senderCost) bestRole = BestRoleForClient.Receiver;

            return new ConnectionEvaluation() { BestEvaluation = bestPlan, BestRoleForClient = bestRole, ConnectionPossibleWithoutBruteForce = true };
        }
    }

}
